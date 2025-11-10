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

namespace Bravellian.Platform.Metrics;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// Writes metrics to SQL Server databases.
/// </summary>
internal sealed class SqlMetricsWriter
{
    private const string SchemaName = "infra";

    /// <summary>
    /// Writes a metric point to an application database.
    /// </summary>
    public static async Task WriteMinutePointAsync(
        string connectionString,
        MetricSeriesKey seriesKey,
        MetricSnapshot snapshot,
        DateTime bucketStartUtc,
        string unit,
        string aggKind,
        string description,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tagsJson = JsonSerializer.Serialize(seriesKey.Tags);
        var tagHash = ComputeTagHash(tagsJson);

        // Upsert series
        var seriesIdParam = new DynamicParameters();
        seriesIdParam.Add("@Name", seriesKey.MetricName);
        seriesIdParam.Add("@Unit", unit);
        seriesIdParam.Add("@AggKind", aggKind);
        seriesIdParam.Add("@Description", description);
        seriesIdParam.Add("@Service", seriesKey.Service);
        seriesIdParam.Add("@InstanceId", seriesKey.InstanceId);
        seriesIdParam.Add("@TagsJson", tagsJson);
        seriesIdParam.Add("@TagHash", tagHash);
        seriesIdParam.Add("@SeriesId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(
            $"[{SchemaName}].[SpUpsertSeries]",
            seriesIdParam,
            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

        var seriesId = seriesIdParam.Get<long>("@SeriesId");

        // Upsert metric point
        var pointParams = new DynamicParameters();
        pointParams.Add("@SeriesId", seriesId);
        pointParams.Add("@BucketStartUtc", bucketStartUtc);
        pointParams.Add("@BucketSecs", 60);
        pointParams.Add("@ValueSum", snapshot.Sum);
        pointParams.Add("@ValueCount", snapshot.Count);
        pointParams.Add("@ValueMin", snapshot.Min);
        pointParams.Add("@ValueMax", snapshot.Max);
        pointParams.Add("@ValueLast", snapshot.Last);
        pointParams.Add("@P50", snapshot.P50);
        pointParams.Add("@P95", snapshot.P95);
        pointParams.Add("@P99", snapshot.P99);

        await connection.ExecuteAsync(
            $"[{SchemaName}].[SpUpsertMetricPointMinute]",
            pointParams,
            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a metric point to the central database.
    /// </summary>
    public static async Task WriteHourlyPointAsync(
        string connectionString,
        MetricSeriesKey seriesKey,
        MetricSnapshot snapshot,
        DateTime bucketStartUtc,
        string unit,
        string aggKind,
        string description,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tagsJson = JsonSerializer.Serialize(seriesKey.Tags);
        var tagHash = ComputeTagHash(tagsJson);

        // Upsert series
        var seriesIdParam = new DynamicParameters();
        seriesIdParam.Add("@Name", seriesKey.MetricName);
        seriesIdParam.Add("@Unit", unit);
        seriesIdParam.Add("@AggKind", aggKind);
        seriesIdParam.Add("@Description", description);
        seriesIdParam.Add("@DatabaseId", seriesKey.DatabaseId);
        seriesIdParam.Add("@Service", seriesKey.Service);
        seriesIdParam.Add("@TagsJson", tagsJson);
        seriesIdParam.Add("@TagHash", tagHash);
        seriesIdParam.Add("@SeriesId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(
            $"[{SchemaName}].[SpUpsertSeriesCentral]",
            seriesIdParam,
            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

        var seriesId = seriesIdParam.Get<long>("@SeriesId");

        // Upsert metric point
        var pointParams = new DynamicParameters();
        pointParams.Add("@SeriesId", seriesId);
        pointParams.Add("@BucketStartUtc", bucketStartUtc);
        pointParams.Add("@BucketSecs", 3600);
        pointParams.Add("@ValueSum", snapshot.Sum);
        pointParams.Add("@ValueCount", snapshot.Count);
        pointParams.Add("@ValueMin", snapshot.Min);
        pointParams.Add("@ValueMax", snapshot.Max);
        pointParams.Add("@ValueLast", snapshot.Last);
        pointParams.Add("@P50", snapshot.P50);
        pointParams.Add("@P95", snapshot.P95);
        pointParams.Add("@P99", snapshot.P99);

        await connection.ExecuteAsync(
            $"[{SchemaName}].[SpUpsertMetricPointHourly]",
            pointParams,
            commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the exporter heartbeat in the central database.
    /// </summary>
    public static async Task UpdateHeartbeatAsync(
        string connectionString,
        string instanceId,
        DateTime lastFlushUtc,
        string? lastError,
        CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            MERGE [{SchemaName}].[ExporterHeartbeat] AS T
            USING (SELECT @InstanceId AS InstanceId) AS S
            ON T.InstanceId = S.InstanceId
            WHEN MATCHED THEN
                UPDATE SET LastFlushUtc = @LastFlushUtc, LastError = @LastError
            WHEN NOT MATCHED THEN
                INSERT (InstanceId, LastFlushUtc, LastError)
                VALUES (@InstanceId, @LastFlushUtc, @LastError);
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
