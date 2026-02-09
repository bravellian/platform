// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of the Inbox pattern for at-most-once message processing.
/// </summary>
internal sealed class PostgresInboxService : IInbox
{
    private readonly PostgresInboxOptions options;
    private readonly string connectionString;
    private readonly ILogger<PostgresInboxService> logger;
    private readonly string upsertSql;
    private readonly string markProcessedSql;
    private readonly string markProcessingSql;
    private readonly string markDeadSql;
    private readonly string enqueueSql;

    public PostgresInboxService(IOptions<PostgresInboxOptions> options, ILogger<PostgresInboxService> logger)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.logger = logger;

        var tableName = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TableName);

        upsertSql = $"""
            INSERT INTO {tableName}
                ("MessageId", "Source", "Hash", "FirstSeenUtc", "CreatedOn", "LastSeenUtc", "Attempts", "AttemptCount", "Status")
            VALUES
                (@MessageId, @Source, @Hash, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, 1, 'Seen')
            ON CONFLICT ("MessageId")
            DO UPDATE SET
                "LastSeenUtc" = CURRENT_TIMESTAMP,
                "Attempts" = {tableName}."Attempts" + 1,
                "AttemptCount" = {tableName}."AttemptCount" + 1
            RETURNING COALESCE("ProcessedOn", "ProcessedUtc") AS "ProcessedUtc";
            """;

        markProcessedSql = $"""
            UPDATE {tableName}
            SET "ProcessedOn" = CURRENT_TIMESTAMP,
                "ProcessedUtc" = CURRENT_TIMESTAMP,
                "Status" = 'Done',
                "LastSeenUtc" = CURRENT_TIMESTAMP
            WHERE "MessageId" = @MessageId;
            """;

        markProcessingSql = $"""
            UPDATE {tableName}
            SET "Status" = 'Processing',
                "LastSeenUtc" = CURRENT_TIMESTAMP
            WHERE "MessageId" = @MessageId;
            """;

        markDeadSql = $"""
            UPDATE {tableName}
            SET "Status" = 'Dead',
                "LastSeenUtc" = CURRENT_TIMESTAMP
            WHERE "MessageId" = @MessageId;
            """;

        enqueueSql = $"""
            INSERT INTO {tableName}
                ("MessageId", "Source", "Topic", "Payload", "Hash", "DueTimeUtc", "DueOn", "FirstSeenUtc", "CreatedOn", "LastSeenUtc", "Attempts", "AttemptCount", "Status")
            VALUES
                (@MessageId, @Source, @Topic, @Payload, @Hash, @DueTimeUtc, @DueTimeUtc, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1, 1, 'Seen')
            ON CONFLICT ("MessageId")
            DO UPDATE SET
                "LastSeenUtc" = CURRENT_TIMESTAMP,
                "Attempts" = {tableName}."Attempts" + 1,
                "AttemptCount" = {tableName}."AttemptCount" + 1,
                "Topic" = COALESCE(EXCLUDED."Topic", {tableName}."Topic"),
                "Payload" = COALESCE(EXCLUDED."Payload", {tableName}."Payload"),
                "DueTimeUtc" = COALESCE(EXCLUDED."DueTimeUtc", {tableName}."DueTimeUtc"),
                "DueOn" = COALESCE(EXCLUDED."DueOn", {tableName}."DueOn");
            """;
    }

    public Task<bool> AlreadyProcessedAsync(
        InboxMessageIdentifier messageId,
        string source,
        CancellationToken cancellationToken)
    {
        return AlreadyProcessedAsync(messageId, source, null, cancellationToken);
    }

    public async Task<bool> AlreadyProcessedAsync(
        InboxMessageIdentifier messageId,
        string source,
        byte[]? hash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var processedUtc = await connection.QuerySingleOrDefaultAsync<DateTime?>(
                upsertSql,
                new { MessageId = messageId.Value, Source = source, Hash = hash }).ConfigureAwait(false);

            var alreadyProcessed = processedUtc.HasValue;

            logger.LogDebug(
                "Message {MessageId} from {Source}: {AlreadyProcessed}",
                messageId,
                source,
                alreadyProcessed ? "already processed" : "first time seen");

            return alreadyProcessed;
        }
        catch (PostgresException ex)
        {
            logger.LogError(
                ex,
                "Failed to check/record message {MessageId} from {Source}",
                messageId,
                source);
            throw;
        }
    }

    public async Task MarkProcessedAsync(
        InboxMessageIdentifier messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                markProcessedSql,
                new { MessageId = messageId.Value }).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                logger.LogWarning(
                    "Message {MessageId} not found when trying to mark processed",
                    messageId);
            }
        }
        catch (PostgresException ex)
        {
            logger.LogError(
                ex,
                "Failed to mark message {MessageId} as processed",
                messageId);
            throw;
        }
    }

    public async Task MarkProcessingAsync(
        InboxMessageIdentifier messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                markProcessingSql,
                new { MessageId = messageId.Value }).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                logger.LogWarning(
                    "Message {MessageId} not found when trying to mark processing",
                    messageId);
            }
        }
        catch (PostgresException ex)
        {
            logger.LogError(
                ex,
                "Failed to mark message {MessageId} as processing",
                messageId);
            throw;
        }
    }

    public async Task MarkDeadAsync(
        InboxMessageIdentifier messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                markDeadSql,
                new { MessageId = messageId.Value }).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                logger.LogWarning(
                    "Message {MessageId} not found when trying to mark dead",
                    messageId);
            }
        }
        catch (PostgresException ex)
        {
            logger.LogError(
                ex,
                "Failed to mark message {MessageId} as dead",
                messageId);
            throw;
        }
    }

    public Task EnqueueAsync(
        string topic,
        string source,
        InboxMessageIdentifier messageId,
        string payload,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, source, messageId, payload, null, null, cancellationToken);
    }

    public Task EnqueueAsync(
        string topic,
        string source,
        InboxMessageIdentifier messageId,
        string payload,
        byte[]? hash,
        CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, source, messageId, payload, hash, null, cancellationToken);
    }

    public async Task EnqueueAsync(
        string topic,
        string source,
        InboxMessageIdentifier messageId,
        string payload,
        byte[]? hash,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                enqueueSql,
                new
                {
                    MessageId = messageId.Value,
                    Source = source,
                    Topic = topic,
                    Payload = payload,
                    Hash = hash,
                    DueTimeUtc = dueTimeUtc?.UtcDateTime,
                }).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            logger.LogError(
                ex,
                "Failed to enqueue message {MessageId} from {Source} to topic {Topic}",
                messageId,
                source,
                topic);
            throw;
        }
    }
}
