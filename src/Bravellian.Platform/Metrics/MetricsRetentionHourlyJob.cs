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
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Scheduled job that cleans up old hourly-level metric data from the central database.
/// </summary>
internal sealed class MetricsRetentionHourlyJob
{
    private readonly ILogger<MetricsRetentionHourlyJob> _logger;
    private readonly MetricsExporterOptions _options;

    public MetricsRetentionHourlyJob(
        ILogger<MetricsRetentionHourlyJob> logger,
        IOptions<MetricsExporterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Executes the retention job for the central database.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.CentralConnectionString))
        {
            _logger.LogWarning("Central connection string not configured, skipping hourly retention");
            return;
        }

        _logger.LogInformation("Starting metrics hourly retention job");

        var cutoffDate = DateTime.UtcNow.AddDays(-_options.HourlyRetentionDays);

        try
        {
            var (server, db) = ParseConnectionInfo(_options.CentralConnectionString);
            var deleted = await DeleteOldHourlyDataAsync(_options.CentralConnectionString, cutoffDate, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Deleted {Count} hourly metric rows from central database ({Server}/{Db})", deleted, server, db);
        }
        catch (Exception ex)
        {
            var (server, db) = ParseConnectionInfo(_options.CentralConnectionString);
            _logger.LogError(ex, "Error deleting hourly metrics from central database ({Server}/{Db})", server, db);
        }
    }

    private static async Task<int> DeleteOldHourlyDataAsync(string connectionString, DateTime cutoffDate, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = """
            DELETE FROM [infra].[MetricPointHourly]
            WHERE BucketStartUtc < @CutoffDate;
            SELECT @@ROWCOUNT;
            """;

        var rowsDeleted = await connection.ExecuteScalarAsync<int>(sql, new { CutoffDate = cutoffDate }).ConfigureAwait(false);
        return rowsDeleted;
    }

    private static (string Server, string Database) ParseConnectionInfo(string cs)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(cs);
            return (builder.DataSource ?? "unknown-server", builder.InitialCatalog ?? "unknown-database");
        }
        catch
        {
            return ("unknown-server", "unknown-database");
        }
    }
}
