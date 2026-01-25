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

using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Writes metrics to PostgreSQL databases.
/// </summary>
internal sealed class PostgresMetricsWriter
{
    public static async Task WriteMinutePointAsync(
        string connectionString,
        string schemaName,
        MetricSeriesKey seriesKey,
        MetricSnapshot snapshot,
        DateTime bucketStartUtc,
        string unit,
        string aggKind,
        string description,
        CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tagsJson = JsonSerializer.Serialize(seriesKey.Tags);
        var tagHash = ComputeTagHash(tagsJson);

        var metricDefTable = PostgresSqlHelper.Qualify(schemaName, "MetricDef");
        var metricSeriesTable = PostgresSqlHelper.Qualify(schemaName, "MetricSeries");
        var metricPointTable = PostgresSqlHelper.Qualify(schemaName, "MetricPointMinute");

        var metricDefId = await connection.ExecuteScalarAsync<int>(
            $"""
            INSERT INTO {metricDefTable} ("Name", "Unit", "AggKind", "Description")
            VALUES (@Name, @Unit, @AggKind, @Description)
            ON CONFLICT ("Name") DO UPDATE
            SET "Unit" = EXCLUDED."Unit",
                "AggKind" = EXCLUDED."AggKind",
                "Description" = EXCLUDED."Description"
            RETURNING "MetricDefId";
            """,
            new { Name = seriesKey.MetricName, Unit = unit, AggKind = aggKind, Description = description }).ConfigureAwait(false);

        var seriesId = await connection.ExecuteScalarAsync<long>(
            $"""
            INSERT INTO {metricSeriesTable} ("MetricDefId", "Service", "InstanceId", "TagsJson", "TagHash")
            VALUES (@MetricDefId, @Service, @InstanceId, @TagsJson, @TagHash)
            ON CONFLICT ("MetricDefId", "Service", "InstanceId", "TagHash") DO UPDATE
            SET "TagsJson" = EXCLUDED."TagsJson"
            RETURNING "SeriesId";
            """,
            new
            {
                MetricDefId = metricDefId,
                Service = seriesKey.Service,
                InstanceId = seriesKey.InstanceId,
                TagsJson = tagsJson,
                TagHash = tagHash,
            }).ConfigureAwait(false);

        var insertPointSql = $"""
            INSERT INTO {metricPointTable} AS mp (
                "SeriesId",
                "BucketStartUtc",
                "BucketSecs",
                "ValueSum",
                "ValueCount",
                "ValueMin",
                "ValueMax",
                "ValueLast",
                "P50",
                "P95",
                "P99"
            )
            VALUES (
                @SeriesId,
                @BucketStartUtc,
                @BucketSecs,
                @ValueSum,
                @ValueCount,
                @ValueMin,
                @ValueMax,
                @ValueLast,
                @P50,
                @P95,
                @P99
            )
            ON CONFLICT ("SeriesId", "BucketStartUtc", "BucketSecs") DO UPDATE
            SET "ValueSum" = COALESCE(mp."ValueSum", 0) + COALESCE(EXCLUDED."ValueSum", 0),
                "ValueCount" = COALESCE(mp."ValueCount", 0) + COALESCE(EXCLUDED."ValueCount", 0),
                "ValueMin" = CASE
                    WHEN mp."ValueMin" IS NULL THEN EXCLUDED."ValueMin"
                    WHEN EXCLUDED."ValueMin" IS NULL THEN mp."ValueMin"
                    WHEN EXCLUDED."ValueMin" < mp."ValueMin" THEN EXCLUDED."ValueMin"
                    ELSE mp."ValueMin"
                END,
                "ValueMax" = CASE
                    WHEN mp."ValueMax" IS NULL THEN EXCLUDED."ValueMax"
                    WHEN EXCLUDED."ValueMax" IS NULL THEN mp."ValueMax"
                    WHEN EXCLUDED."ValueMax" > mp."ValueMax" THEN EXCLUDED."ValueMax"
                    ELSE mp."ValueMax"
                END,
                "ValueLast" = EXCLUDED."ValueLast",
                "InsertedUtc" = CURRENT_TIMESTAMP;
            """;

        await connection.ExecuteAsync(
            insertPointSql,
            new
            {
                SeriesId = seriesId,
                BucketStartUtc = bucketStartUtc,
                BucketSecs = 60,
                ValueSum = snapshot.Sum,
                ValueCount = snapshot.Count,
                ValueMin = snapshot.Min,
                ValueMax = snapshot.Max,
                ValueLast = snapshot.Last,
                P50 = snapshot.P50,
                P95 = snapshot.P95,
                P99 = snapshot.P99,
            }).ConfigureAwait(false);
    }

    public static async Task WriteHourlyPointAsync(
        string connectionString,
        string schemaName,
        MetricSeriesKey seriesKey,
        MetricSnapshot snapshot,
        DateTime bucketStartUtc,
        string unit,
        string aggKind,
        string description,
        CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tagsJson = JsonSerializer.Serialize(seriesKey.Tags);
        var tagHash = ComputeTagHash(tagsJson);

        var metricDefTable = PostgresSqlHelper.Qualify(schemaName, "MetricDef");
        var metricSeriesTable = PostgresSqlHelper.Qualify(schemaName, "MetricSeries");
        var metricPointTable = PostgresSqlHelper.Qualify(schemaName, "MetricPointHourly");

        var metricDefId = await connection.ExecuteScalarAsync<int>(
            $"""
            INSERT INTO {metricDefTable} ("Name", "Unit", "AggKind", "Description")
            VALUES (@Name, @Unit, @AggKind, @Description)
            ON CONFLICT ("Name") DO UPDATE
            SET "Unit" = EXCLUDED."Unit",
                "AggKind" = EXCLUDED."AggKind",
                "Description" = EXCLUDED."Description"
            RETURNING "MetricDefId";
            """,
            new { Name = seriesKey.MetricName, Unit = unit, AggKind = aggKind, Description = description }).ConfigureAwait(false);

        var seriesId = await connection.ExecuteScalarAsync<long>(
            $"""
            INSERT INTO {metricSeriesTable} ("MetricDefId", "DatabaseId", "Service", "TagsJson", "TagHash")
            VALUES (@MetricDefId, @DatabaseId, @Service, @TagsJson, @TagHash)
            ON CONFLICT ("MetricDefId", "DatabaseId", "Service", "TagHash") DO UPDATE
            SET "TagsJson" = EXCLUDED."TagsJson"
            RETURNING "SeriesId";
            """,
            new
            {
                MetricDefId = metricDefId,
                DatabaseId = seriesKey.DatabaseId,
                Service = seriesKey.Service,
                TagsJson = tagsJson,
                TagHash = tagHash,
            }).ConfigureAwait(false);

        var insertPointSql = $"""
            INSERT INTO {metricPointTable} AS mp (
                "SeriesId",
                "BucketStartUtc",
                "BucketSecs",
                "ValueSum",
                "ValueCount",
                "ValueMin",
                "ValueMax",
                "ValueLast",
                "P50",
                "P95",
                "P99"
            )
            VALUES (
                @SeriesId,
                @BucketStartUtc,
                @BucketSecs,
                @ValueSum,
                @ValueCount,
                @ValueMin,
                @ValueMax,
                @ValueLast,
                @P50,
                @P95,
                @P99
            )
            ON CONFLICT ("SeriesId", "BucketStartUtc", "BucketSecs") DO UPDATE
            SET "ValueSum" = COALESCE(mp."ValueSum", 0) + COALESCE(EXCLUDED."ValueSum", 0),
                "ValueCount" = COALESCE(mp."ValueCount", 0) + COALESCE(EXCLUDED."ValueCount", 0),
                "ValueMin" = CASE
                    WHEN mp."ValueMin" IS NULL THEN EXCLUDED."ValueMin"
                    WHEN EXCLUDED."ValueMin" IS NULL THEN mp."ValueMin"
                    WHEN EXCLUDED."ValueMin" < mp."ValueMin" THEN EXCLUDED."ValueMin"
                    ELSE mp."ValueMin"
                END,
                "ValueMax" = CASE
                    WHEN mp."ValueMax" IS NULL THEN EXCLUDED."ValueMax"
                    WHEN EXCLUDED."ValueMax" IS NULL THEN mp."ValueMax"
                    WHEN EXCLUDED."ValueMax" > mp."ValueMax" THEN EXCLUDED."ValueMax"
                    ELSE mp."ValueMax"
                END,
                "ValueLast" = EXCLUDED."ValueLast",
                "InsertedUtc" = CURRENT_TIMESTAMP;
            """;

        await connection.ExecuteAsync(
            insertPointSql,
            new
            {
                SeriesId = seriesId,
                BucketStartUtc = bucketStartUtc,
                BucketSecs = 3600,
                ValueSum = snapshot.Sum,
                ValueCount = snapshot.Count,
                ValueMin = snapshot.Min,
                ValueMax = snapshot.Max,
                ValueLast = snapshot.Last,
                P50 = snapshot.P50,
                P95 = snapshot.P95,
                P99 = snapshot.P99,
            }).ConfigureAwait(false);
    }

    public static async Task UpdateHeartbeatAsync(
        string connectionString,
        string schemaName,
        string instanceId,
        DateTime lastFlushUtc,
        string? lastError,
        CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var heartbeatTable = PostgresSqlHelper.Qualify(schemaName, "ExporterHeartbeat");
        var sql = $"""
            INSERT INTO {heartbeatTable} ("InstanceId", "LastFlushUtc", "LastError")
            VALUES (@InstanceId, @LastFlushUtc, @LastError)
            ON CONFLICT ("InstanceId") DO UPDATE
            SET "LastFlushUtc" = EXCLUDED."LastFlushUtc",
                "LastError" = EXCLUDED."LastError";
            """;

        await connection.ExecuteAsync(sql, new
        {
            InstanceId = instanceId,
            LastFlushUtc = lastFlushUtc,
            LastError = lastError,
        }).ConfigureAwait(false);
    }

    private static byte[] ComputeTagHash(string tagsJson)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(tagsJson));
    }
}
