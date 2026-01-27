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

using System.Data;
using System.Text.Json;
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Operations;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of <see cref="IOperationTracker"/>.
/// </summary>
public sealed class PostgresOperationTracker : IOperationTracker
{
    private readonly PostgresOperationOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PostgresOperationTracker> logger;
    private readonly string qualifiedOperationsTable;
    private readonly string qualifiedOperationEventsTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresOperationTracker"/> class.
    /// </summary>
    /// <param name="options">Postgres options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public PostgresOperationTracker(
        IOptions<PostgresOperationOptions> options,
        TimeProvider timeProvider,
        ILogger<PostgresOperationTracker> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        qualifiedOperationsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.OperationsTable);
        qualifiedOperationEventsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.OperationEventsTable);
    }

    /// <inheritdoc />
    public async Task<OperationId> StartAsync(
        string name,
        CorrelationContext? correlationContext,
        OperationId? parentOperationId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Operation name is required.", nameof(name));
        }

        var operationId = OperationId.NewId();
        var now = timeProvider.GetUtcNow();
        var sql = $"""
            INSERT INTO {qualifiedOperationsTable} (
                "OperationId",
                "Name",
                "Status",
                "StartedAtUtc",
                "UpdatedAtUtc",
                "CompletedAtUtc",
                "PercentComplete",
                "Message",
                "CorrelationId",
                "CausationId",
                "TraceId",
                "SpanId",
                "CorrelationCreatedAtUtc",
                "CorrelationTagsJson",
                "ParentOperationId",
                "TagsJson"
            )
            VALUES (
                @OperationId,
                @Name,
                @Status,
                @StartedAtUtc,
                @UpdatedAtUtc,
                @CompletedAtUtc,
                @PercentComplete,
                @Message,
                @CorrelationId,
                @CausationId,
                @TraceId,
                @SpanId,
                @CorrelationCreatedAtUtc,
                @CorrelationTagsJson,
                @ParentOperationId,
                @TagsJson
            )
            """;

        var parameters = new
        {
            OperationId = operationId.Value,
            Name = name.Trim(),
            Status = (short)OperationStatus.Pending,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = (DateTimeOffset?)null,
            PercentComplete = (double?)null,
            Message = (string?)null,
            CorrelationId = correlationContext?.CorrelationId.Value,
            CausationId = correlationContext?.CausationId?.Value,
            TraceId = correlationContext?.TraceId,
            SpanId = correlationContext?.SpanId,
            CorrelationCreatedAtUtc = correlationContext?.CreatedAtUtc,
            CorrelationTagsJson = SerializeTags(correlationContext?.Tags),
            ParentOperationId = parentOperationId?.Value,
            TagsJson = SerializeTags(tags),
        };

        try
        {
            using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
            PostgresOperationMetrics.RecordStarted();
            return operationId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start operation {OperationId}.", operationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateProgressAsync(
        OperationId operationId,
        double? percentComplete,
        string? message,
        CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var row = await GetRowForUpdateAsync(connection, transaction, operationId).ConfigureAwait(false);

        if (row is null)
        {
            throw new InvalidOperationException($"Operation '{operationId}' was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var nextStatus = row.Status is OperationStatus.Pending ? OperationStatus.Running : row.Status;

        var updateSql = $"""
            UPDATE {qualifiedOperationsTable}
            SET "Status" = @Status,
                "UpdatedAtUtc" = @UpdatedAtUtc,
                "PercentComplete" = @PercentComplete,
                "Message" = @Message,
                "RowVersion" = "RowVersion" + 1
            WHERE "OperationId" = @OperationId AND "RowVersion" = @RowVersion
            """;

        var updated = await connection.ExecuteAsync(
            updateSql,
            new
            {
                OperationId = operationId.Value,
                Status = (short)nextStatus,
                UpdatedAtUtc = now,
                PercentComplete = percentComplete is null ? (double?)null : Math.Clamp(percentComplete.Value, 0, 100),
                Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
                RowVersion = row.RowVersion,
            },
            transaction).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new DBConcurrencyException($"Operation '{operationId}' was modified by another process.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        PostgresOperationMetrics.RecordProgressUpdated();
    }

    /// <inheritdoc />
    public async Task AddEventAsync(
        OperationId operationId,
        string kind,
        string message,
        string? dataJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Event kind is required.", nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Event message is required.", nameof(message));
        }

        var now = timeProvider.GetUtcNow();
        var sql = $"""
            INSERT INTO {qualifiedOperationEventsTable}
            (
                "OperationId",
                "OccurredAtUtc",
                "Kind",
                "Message",
                "DataJson"
            )
            VALUES
            (
                @OperationId,
                @OccurredAtUtc,
                @Kind,
                @Message,
                @DataJson
            )
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(
            sql,
            new
            {
                OperationId = operationId.Value,
                OccurredAtUtc = now,
                Kind = kind.Trim(),
                Message = message.Trim(),
                DataJson = dataJson,
            }).ConfigureAwait(false);
        PostgresOperationMetrics.RecordEventAdded(kind);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        OperationId operationId,
        OperationStatus status,
        string? message,
        CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var row = await GetRowForUpdateAsync(connection, transaction, operationId).ConfigureAwait(false);

        if (row is null)
        {
            throw new InvalidOperationException($"Operation '{operationId}' was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var updateSql = $"""
            UPDATE {qualifiedOperationsTable}
            SET "Status" = @Status,
                "UpdatedAtUtc" = @UpdatedAtUtc,
                "CompletedAtUtc" = @CompletedAtUtc,
                "Message" = @Message,
                "RowVersion" = "RowVersion" + 1
            WHERE "OperationId" = @OperationId AND "RowVersion" = @RowVersion
            """;

        var updated = await connection.ExecuteAsync(
            updateSql,
            new
            {
                OperationId = operationId.Value,
                Status = (short)status,
                UpdatedAtUtc = now,
                CompletedAtUtc = status is OperationStatus.Succeeded or OperationStatus.Failed or OperationStatus.Canceled or OperationStatus.Stalled
                    ? now
                    : (DateTimeOffset?)null,
                Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
                RowVersion = row.RowVersion,
            },
            transaction).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new DBConcurrencyException($"Operation '{operationId}' was modified by another process.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        PostgresOperationMetrics.RecordCompleted(status);
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot?> GetSnapshotAsync(OperationId operationId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                "OperationId",
                "Name",
                "Status",
                "StartedAtUtc",
                "UpdatedAtUtc",
                "CompletedAtUtc",
                "PercentComplete",
                "Message",
                "CorrelationId",
                "CausationId",
                "TraceId",
                "SpanId",
                "CorrelationCreatedAtUtc",
                "CorrelationTagsJson",
                "ParentOperationId",
                "TagsJson",
                "RowVersion"
            FROM {qualifiedOperationsTable}
            WHERE "OperationId" = @OperationId
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<OperationRow>(
            sql,
            new { OperationId = operationId.Value }).ConfigureAwait(false);

        PostgresOperationMetrics.RecordSnapshotRead(row != null);
        return row == null ? null : MapSnapshot(row);
    }

    private async Task<OperationRow?> GetRowForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperationId operationId)
    {
        var sql = $"""
            SELECT
                "OperationId",
                "Name",
                "Status",
                "StartedAtUtc",
                "UpdatedAtUtc",
                "CompletedAtUtc",
                "PercentComplete",
                "Message",
                "CorrelationId",
                "CausationId",
                "TraceId",
                "SpanId",
                "CorrelationCreatedAtUtc",
                "CorrelationTagsJson",
                "ParentOperationId",
                "TagsJson",
                "RowVersion"
            FROM {qualifiedOperationsTable}
            WHERE "OperationId" = @OperationId
            FOR UPDATE
            """;

        return await connection.QuerySingleOrDefaultAsync<OperationRow>(
            sql,
            new { OperationId = operationId.Value },
            transaction).ConfigureAwait(false);
    }

    private static OperationSnapshot MapSnapshot(OperationRow row)
    {
        return new OperationSnapshot(
            new OperationId(row.OperationId),
            row.Name,
            row.Status,
            row.StartedAtUtc,
            row.UpdatedAtUtc,
            row.CompletedAtUtc,
            row.PercentComplete,
            row.Message,
            BuildCorrelationContext(row),
            string.IsNullOrWhiteSpace(row.ParentOperationId) ? null : new OperationId(row.ParentOperationId),
            DeserializeTags(row.TagsJson));
    }

    private static CorrelationContext? BuildCorrelationContext(OperationRow row)
    {
        if (string.IsNullOrWhiteSpace(row.CorrelationId))
        {
            return null;
        }

        return new CorrelationContext(
            new CorrelationId(row.CorrelationId),
            string.IsNullOrWhiteSpace(row.CausationId) ? null : new CorrelationId(row.CausationId),
            string.IsNullOrWhiteSpace(row.TraceId) ? null : row.TraceId,
            string.IsNullOrWhiteSpace(row.SpanId) ? null : row.SpanId,
            row.CorrelationCreatedAtUtc ?? DateTimeOffset.UnixEpoch,
            DeserializeTags(row.CorrelationTagsJson));
    }

    private static string? SerializeTags(IReadOnlyDictionary<string, string>? tags)
    {
        return tags is null ? null : JsonSerializer.Serialize(tags);
    }

    private static IReadOnlyDictionary<string, string>? DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }

    private sealed class OperationRow
    {
        public string OperationId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public OperationStatus Status { get; init; }
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public double? PercentComplete { get; init; }
        public string? Message { get; init; }
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public string? TraceId { get; init; }
        public string? SpanId { get; init; }
        public DateTimeOffset? CorrelationCreatedAtUtc { get; init; }
        public string? CorrelationTagsJson { get; init; }
        public string? ParentOperationId { get; init; }
        public string? TagsJson { get; init; }
        public long RowVersion { get; init; }
    }
}
