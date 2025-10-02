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

namespace Bravellian.Platform;

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

/// <summary>
/// SQL Server implementation of the Inbox pattern for at-most-once message processing.
/// </summary>
internal class SqlInboxService : IInbox
{
    private readonly SqlInboxOptions options;
    private readonly string connectionString;
    private readonly ILogger<SqlInboxService> logger;
    private readonly string upsertSql;
    private readonly string markProcessedSql;
    private readonly string markProcessingSql;
    private readonly string markDeadSql;
    private readonly string enqueueSql;

    public SqlInboxService(IOptions<SqlInboxOptions> options, ILogger<SqlInboxService> logger)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        this.logger = logger;

        // Build SQL queries using configured schema and table names
        var tableName = $"[{this.options.SchemaName}].[{this.options.TableName}]";

        // MERGE statement for atomic upsert operation with concurrency safety
        this.upsertSql = $@"
            MERGE {tableName} AS target
            USING (SELECT @MessageId AS MessageId, @Source AS Source, @Hash AS Hash) AS source
                ON target.MessageId = source.MessageId
            WHEN MATCHED THEN
                UPDATE SET 
                    LastSeenUtc = GETUTCDATE(),
                    Attempts = Attempts + 1
            WHEN NOT MATCHED THEN
                INSERT (MessageId, Source, Hash, FirstSeenUtc, LastSeenUtc, Attempts)
                VALUES (source.MessageId, source.Source, source.Hash, GETUTCDATE(), GETUTCDATE(), 1)
            OUTPUT ISNULL(inserted.ProcessedUtc, deleted.ProcessedUtc) AS ProcessedUtc;";

        this.markProcessedSql = $@"
            UPDATE {tableName}
            SET ProcessedUtc = GETUTCDATE(),
                Status = 'Done',
                LastSeenUtc = GETUTCDATE()
            WHERE MessageId = @MessageId;";

        this.markProcessingSql = $@"
            UPDATE {tableName}
            SET Status = 'Processing',
                LastSeenUtc = GETUTCDATE()
            WHERE MessageId = @MessageId;";

        this.markDeadSql = $@"
            UPDATE {tableName}
            SET Status = 'Dead',
                LastSeenUtc = GETUTCDATE()
            WHERE MessageId = @MessageId;";

        this.enqueueSql = $@"
            MERGE {tableName} AS target
            USING (SELECT @MessageId AS MessageId, @Source AS Source, @Topic AS Topic, @Payload AS Payload, @Hash AS Hash) AS source
                ON target.MessageId = source.MessageId
            WHEN MATCHED THEN
                UPDATE SET 
                    LastSeenUtc = GETUTCDATE(),
                    Attempts = Attempts + 1,
                    Topic = COALESCE(source.Topic, target.Topic),
                    Payload = COALESCE(source.Payload, target.Payload)
            WHEN NOT MATCHED THEN
                INSERT (MessageId, Source, Topic, Payload, Hash, FirstSeenUtc, LastSeenUtc, Attempts, Status)
                VALUES (source.MessageId, source.Source, source.Topic, source.Payload, source.Hash, GETUTCDATE(), GETUTCDATE(), 1, 'Seen');";
    }

    public async Task<bool> AlreadyProcessedAsync(
        string messageId,
        string source,
        byte[]? hash = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Execute MERGE and get the ProcessedUtc value
            var processedUtc = await connection.QuerySingleOrDefaultAsync<DateTime?>(
                this.upsertSql,
                new { MessageId = messageId, Source = source, Hash = hash }).ConfigureAwait(false);

            // If ProcessedUtc has a value, the message was already processed
            var alreadyProcessed = processedUtc.HasValue;

            this.logger.LogDebug(
                "Message {MessageId} from {Source}: {AlreadyProcessed}",
                messageId,
                source,
                alreadyProcessed ? "already processed" : "first time seen");

            return alreadyProcessed;
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, 
                "Failed to check/record message {MessageId} from {Source}", 
                messageId, source);
            throw;
        }
    }

    public async Task MarkProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                this.markProcessedSql,
                new { MessageId = messageId }).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                this.logger.LogWarning(
                    "Attempted to mark message {MessageId} as processed, but no rows were affected. Message may not exist.",
                    messageId);
            }
            else
            {
                this.logger.LogDebug(
                    "Message {MessageId} marked as processed",
                    messageId);
            }
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, 
                "Failed to mark message {MessageId} as processed", 
                messageId);
            throw;
        }
    }

    public async Task MarkProcessingAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                this.markProcessingSql,
                new { MessageId = messageId }).ConfigureAwait(false);

            this.logger.LogDebug(
                "Message {MessageId} marked as processing (rows affected: {RowsAffected})",
                messageId, rowsAffected);
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, 
                "Failed to mark message {MessageId} as processing", 
                messageId);
            throw;
        }
    }

    public async Task MarkDeadAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(
                this.markDeadSql,
                new { MessageId = messageId }).ConfigureAwait(false);

            this.logger.LogWarning(
                "Message {MessageId} marked as dead/poison (rows affected: {RowsAffected})",
                messageId, rowsAffected);
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, 
                "Failed to mark message {MessageId} as dead", 
                messageId);
            throw;
        }
    }

    public async Task EnqueueAsync(
        string topic,
        string source,
        string messageId,
        string payload,
        byte[]? hash = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrEmpty(payload))
        {
            throw new ArgumentException("Payload cannot be null or empty", nameof(payload));
        }

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                this.enqueueSql,
                new 
                { 
                    MessageId = messageId, 
                    Source = source, 
                    Topic = topic, 
                    Payload = payload, 
                    Hash = hash 
                }).ConfigureAwait(false);

            this.logger.LogDebug(
                "Enqueued message {MessageId} with topic '{Topic}' from source '{Source}'",
                messageId, topic, source);
        }
        catch (SqlException ex)
        {
            this.logger.LogError(ex, 
                "Failed to enqueue message {MessageId} with topic '{Topic}' from source '{Source}'", 
                messageId, topic, source);
            throw;
        }
    }
}