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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server implementation of <see cref="IEmailOutboxStore"/>.
/// </summary>
public sealed class SqlEmailOutboxStore : IEmailOutboxStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SqlEmailOutboxOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlEmailOutboxStore> logger;
    private readonly string qualifiedTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlEmailOutboxStore"/> class.
    /// </summary>
    /// <param name="options">SQL Server email outbox options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlEmailOutboxStore(
        IOptions<SqlEmailOutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlEmailOutboxStore> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        qualifiedTable = $"[{this.options.SchemaName}].[{this.options.TableName}]";
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
            SELECT TOP 1 1
            FROM {qualifiedTable}
            WHERE ProviderName = @ProviderName AND MessageKey = @MessageKey
            """;

        using var connection = new SqlConnection(options.ConnectionString);
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
            IF NOT EXISTS (
                SELECT 1
                FROM {qualifiedTable}
                WHERE ProviderName = @ProviderName AND MessageKey = @MessageKey
            )
            BEGIN
                INSERT INTO {qualifiedTable}
                (
                    EmailOutboxId,
                    ProviderName,
                    MessageKey,
                    Payload,
                    EnqueuedAtUtc,
                    DueTimeUtc,
                    AttemptCount,
                    Status,
                    FailureReason
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
            END
            """;

        using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
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
                    Status = (byte)EmailOutboxStatus.Pending,
                    FailureReason = (string?)null,
                }).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            logger.LogDebug(
                ex,
                "Email outbox item with provider {Provider} and message key {MessageKey} already exists.",
                item.ProviderName,
                item.MessageKey);
        }
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
            ;WITH cte AS (
                SELECT TOP (@MaxItems) *
                FROM {qualifiedTable} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = @PendingStatus
                  AND (DueTimeUtc IS NULL OR DueTimeUtc <= @NowUtc)
                ORDER BY EnqueuedAtUtc
            )
            UPDATE cte
            SET Status = @ProcessingStatus,
                AttemptCount = AttemptCount + 1
            OUTPUT
                inserted.EmailOutboxId,
                inserted.ProviderName,
                inserted.MessageKey,
                inserted.Payload,
                inserted.EnqueuedAtUtc,
                inserted.DueTimeUtc,
                inserted.AttemptCount
            """;

        using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<EmailOutboxRow>(
            sql,
            new
            {
                MaxItems = maxItems,
                PendingStatus = (byte)EmailOutboxStatus.Pending,
                ProcessingStatus = (byte)EmailOutboxStatus.Processing,
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
            SET Status = @SucceededStatus,
                FailureReason = NULL
            WHERE EmailOutboxId = @EmailOutboxId
            """;

        using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            sql,
            new
            {
                EmailOutboxId = outboxId,
                SucceededStatus = (byte)EmailOutboxStatus.Succeeded,
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid outboxId, string? failureReason, CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {qualifiedTable}
            SET Status = @FailedStatus,
                FailureReason = @FailureReason
            WHERE EmailOutboxId = @EmailOutboxId
            """;

        using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            sql,
            new
            {
                EmailOutboxId = outboxId,
                FailedStatus = (byte)EmailOutboxStatus.Failed,
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
