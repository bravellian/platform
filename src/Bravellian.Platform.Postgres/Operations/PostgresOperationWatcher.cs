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
/// PostgreSQL implementation of <see cref="IOperationWatcher"/>.
/// </summary>
public sealed class PostgresOperationWatcher : IOperationWatcher
{
    private readonly PostgresOperationOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PostgresOperationWatcher> logger;
    private readonly string qualifiedOperationsTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresOperationWatcher"/> class.
    /// </summary>
    /// <param name="options">Postgres options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public PostgresOperationWatcher(
        IOptions<PostgresOperationOptions> options,
        TimeProvider timeProvider,
        ILogger<PostgresOperationWatcher> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        qualifiedOperationsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.OperationsTable);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationSnapshot>> FindStalledAsync(TimeSpan threshold, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().Subtract(threshold);
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
            WHERE "UpdatedAtUtc" < @Cutoff
              AND "Status" IN (@Pending, @Running)
            """;

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<OperationRow>(
            sql,
            new
            {
                Cutoff = cutoff,
                Pending = (short)OperationStatus.Pending,
                Running = (short)OperationStatus.Running,
            }).ConfigureAwait(false);

        return rows.Select(MapSnapshot).ToList();
    }

    /// <inheritdoc />
    public async Task MarkStalledAsync(OperationId operationId, CancellationToken cancellationToken)
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
                "RowVersion" = "RowVersion" + 1
            WHERE "OperationId" = @OperationId AND "RowVersion" = @RowVersion
            """;

        var updated = await connection.ExecuteAsync(
            updateSql,
            new
            {
                OperationId = operationId.Value,
                Status = (short)OperationStatus.Stalled,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                RowVersion = row.RowVersion,
            },
            transaction).ConfigureAwait(false);

        if (updated == 0)
        {
            throw new DBConcurrencyException($"Operation '{operationId}' was modified by another process.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Operation {OperationId} marked as stalled.", operationId);
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
