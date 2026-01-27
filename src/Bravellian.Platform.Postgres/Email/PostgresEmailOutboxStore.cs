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

using System.Text.Json;
using System.Text.Json.Serialization;
using Bravellian.Platform.Email;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of <see cref="IEmailOutboxStore"/>.
/// </summary>
public sealed class PostgresEmailOutboxStore : IEmailOutboxStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PostgresEmailOutboxOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PostgresEmailOutboxStore> logger;
    private readonly string qualifiedTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresEmailOutboxStore"/> class.
    /// </summary>
    /// <param name="options">Postgres email outbox options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public PostgresEmailOutboxStore(
        IOptions<PostgresEmailOutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<PostgresEmailOutboxStore> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        qualifiedTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TableName);
    }

    /// <inheritdoc />
    public async Task<bool> AlreadyEnqueuedAsync(
        string messageKey,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
        {
            throw new ArgumentException("Message key is required.", nameof(messageKey));
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        var sql = $"""
            SELECT 1
            FROM {qualifiedTable}
            WHERE "ProviderName" = @ProviderName AND "MessageKey" = @MessageKey
            LIMIT 1
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var result = await connection.ExecuteScalarAsync<int?>(
            sql,
            new
            {
                ProviderName = providerName,
                MessageKey = messageKey,
            }).ConfigureAwait(false);

        return result.HasValue;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(EmailOutboxItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var payload = JsonSerializer.Serialize(item.Message, SerializerOptions);
        var sql = $"""
            INSERT INTO {qualifiedTable}
            (
                "EmailOutboxId",
                "ProviderName",
                "MessageKey",
                "Payload",
                "EnqueuedAtUtc",
                "DueTimeUtc",
                "AttemptCount",
                "Status",
                "FailureReason"
            )
            VALUES
            (
                @EmailOutboxId,
                @ProviderName,
                @MessageKey,
                @Payload,
                @EnqueuedAtUtc,
                @DueTimeUtc,
                @AttemptCount,
                @Status,
                @FailureReason
            )
            ON CONFLICT ("ProviderName", "MessageKey") DO NOTHING
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            sql,
            new
            {
                EmailOutboxId = item.Id,
                item.ProviderName,
                item.MessageKey,
                Payload = payload,
                item.EnqueuedAtUtc,
                item.DueTimeUtc,
                item.AttemptCount,
                Status = (short)EmailOutboxStatus.Pending,
                FailureReason = (string?)null,
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailOutboxItem>> DequeueAsync(int maxItems, CancellationToken cancellationToken)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Batch size must be greater than zero.");
        }

        var now = timeProvider.GetUtcNow();
        var sql = $"""
            WITH cte AS (
                SELECT "EmailOutboxId"
                FROM {qualifiedTable}
                WHERE "Status" = @PendingStatus
                  AND ("DueTimeUtc" IS NULL OR "DueTimeUtc" <= @NowUtc)
                ORDER BY "EnqueuedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT @MaxItems
            )
            UPDATE {qualifiedTable} AS t
            SET "Status" = @ProcessingStatus,
                "AttemptCount" = "AttemptCount" + 1
            FROM cte
            WHERE t."EmailOutboxId" = cte."EmailOutboxId"
            RETURNING
                t."EmailOutboxId",
                t."ProviderName",
                t."MessageKey",
                t."Payload",
                t."EnqueuedAtUtc",
                t."DueTimeUtc",
                t."AttemptCount"
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<EmailOutboxRow>(
            sql,
            new
            {
                MaxItems = maxItems,
                PendingStatus = (short)EmailOutboxStatus.Pending,
                ProcessingStatus = (short)EmailOutboxStatus.Processing,
                NowUtc = now,
            }).ConfigureAwait(false);

        var items = new List<EmailOutboxItem>();
        foreach (var row in rows)
        {
            var message = Deserialize(row.Payload, row.EmailOutboxId);
            items.Add(new EmailOutboxItem(
                row.EmailOutboxId,
                row.ProviderName,
                row.MessageKey,
                message,
                row.EnqueuedAtUtc,
                row.DueTimeUtc,
                row.AttemptCount));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task MarkSucceededAsync(Guid outboxId, CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {qualifiedTable}
            SET "Status" = @SucceededStatus,
                "FailureReason" = NULL
            WHERE "EmailOutboxId" = @EmailOutboxId
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            sql,
            new
            {
                EmailOutboxId = outboxId,
                SucceededStatus = (short)EmailOutboxStatus.Succeeded,
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid outboxId, string? failureReason, CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {qualifiedTable}
            SET "Status" = @FailedStatus,
                "FailureReason" = @FailureReason
            WHERE "EmailOutboxId" = @EmailOutboxId
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            sql,
            new
            {
                EmailOutboxId = outboxId,
                FailedStatus = (short)EmailOutboxStatus.Failed,
                FailureReason = failureReason,
            }).ConfigureAwait(false);
    }

    private OutboundEmailMessage Deserialize(string payload, Guid outboxId)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException($"Email outbox {outboxId} payload was empty.");
        }

        try
        {
            var message = JsonSerializer.Deserialize<OutboundEmailMessage>(payload, SerializerOptions);
            return message ?? throw new JsonException("Email payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize email outbox payload for {EmailOutboxId}.", outboxId);
            throw;
        }
    }

    private sealed class EmailOutboxRow
    {
        public Guid EmailOutboxId { get; init; }
        public string ProviderName { get; init; } = string.Empty;
        public string MessageKey { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public DateTimeOffset EnqueuedAtUtc { get; init; }
        public DateTimeOffset? DueTimeUtc { get; init; }
        public int AttemptCount { get; init; }
    }
}
