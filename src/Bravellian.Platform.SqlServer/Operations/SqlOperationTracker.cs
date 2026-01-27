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
using System.Data.Common;
using System.Text.Json;
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Operations;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server implementation of <see cref="IOperationTracker"/>.
/// </summary>
public sealed class SqlOperationTracker : IOperationTracker
{
    private readonly SqlOperationOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqlOperationTracker> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlOperationTracker"/> class.
    /// </summary>
    /// <param name="options">SQL Server options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlOperationTracker(
        IOptions<SqlOperationOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlOperationTracker> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            INSERT INTO [{options.SchemaName}].[{options.OperationsTable}] (
                OperationId,
                Name,
                Status,
                StartedAtUtc,
                UpdatedAtUtc,
                CompletedAtUtc,
                PercentComplete,
                Message,
                CorrelationId,
                CausationId,
                TraceId,
                SpanId,
                CorrelationCreatedAtUtc,
                CorrelationTagsJson,
                ParentOperationId,
                TagsJson
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
            Status = (byte)OperationStatus.Pending,
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
            var connection = new SqlConnection(options.ConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
                SqlOperationMetrics.RecordStarted();
                return operationId;
            }
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
        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var row = await GetRowForUpdateAsync(connection, transaction, operationId, cancellationToken).ConfigureAwait(false);

                if (row is null)
                {
                    throw new InvalidOperationException($"Operation '{operationId}' was not found.");
                }

                var now = timeProvider.GetUtcNow();
                var nextStatus = row.Status is OperationStatus.Pending ? OperationStatus.Running : row.Status;

                var updateSql = $"""
            UPDATE [{options.SchemaName}].[{options.OperationsTable}]
            SET Status = @Status,
                UpdatedAtUtc = @UpdatedAtUtc,
                PercentComplete = @PercentComplete,
                Message = @Message
            WHERE OperationId = @OperationId AND RowVersion = @RowVersion
            """;

                var updated = await connection.ExecuteAsync(
                    updateSql,
                    new
                    {
                        OperationId = operationId.Value,
                        Status = (byte)nextStatus,
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
                SqlOperationMetrics.RecordProgressUpdated();
            }
        }
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
            INSERT INTO [{options.SchemaName}].[{options.OperationEventsTable}]
            (
                OperationId,
                OccurredAtUtc,
                Kind,
                Message,
                DataJson
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

        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
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
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        OperationId operationId,
        OperationStatus status,
        string? message,
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var row = await GetRowForUpdateAsync(connection, transaction, operationId, cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    throw new InvalidOperationException($"Operation '{operationId}' was not found.");
                }

                var now = timeProvider.GetUtcNow();
                var updateSql = $"""
            UPDATE [{options.SchemaName}].[{options.OperationsTable}]
            SET Status = @Status,
                UpdatedAtUtc = @UpdatedAtUtc,
                CompletedAtUtc = @CompletedAtUtc,
                Message = @Message
            WHERE OperationId = @OperationId AND RowVersion = @RowVersion
            """;

                var updated = await connection.ExecuteAsync(
                    updateSql,
                    new
                    {
                        OperationId = operationId.Value,
                        Status = (byte)status,
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
                SqlOperationMetrics.RecordCompleted(status);
            }
        }
    }

    /// <inheritdoc />
    public async Task<OperationSnapshot?> GetSnapshotAsync(OperationId operationId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                OperationId,
                Name,
                Status,
                StartedAtUtc,
                UpdatedAtUtc,
                CompletedAtUtc,
                PercentComplete,
                Message,
                CorrelationId,
                CausationId,
                TraceId,
                SpanId,
                CorrelationCreatedAtUtc,
                CorrelationTagsJson,
                ParentOperationId,
                TagsJson,
                RowVersion
            FROM [{options.SchemaName}].[{options.OperationsTable}]
            WHERE OperationId = @OperationId
            """;
        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var row = await connection.QuerySingleOrDefaultAsync<OperationRow>(
                sql,
                new { OperationId = operationId.Value }).ConfigureAwait(false);

            SqlOperationMetrics.RecordSnapshotRead(row != null);
            return row == null ? null : MapSnapshot(row);
        }
    }

    private async Task<OperationRow?> GetRowForUpdateAsync(
        SqlConnection connection,
        DbTransaction transaction,
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                OperationId,
                Name,
                Status,
                StartedAtUtc,
                UpdatedAtUtc,
                CompletedAtUtc,
                PercentComplete,
                Message,
                CorrelationId,
                CausationId,
                TraceId,
                SpanId,
                CorrelationCreatedAtUtc,
                CorrelationTagsJson,
                ParentOperationId,
                TagsJson,
                RowVersion
            FROM [{options.SchemaName}].[{options.OperationsTable}] WITH (UPDLOCK, ROWLOCK)
            WHERE OperationId = @OperationId
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
        public byte[] RowVersion { get; init; } = Array.Empty<byte>();
    }
}

