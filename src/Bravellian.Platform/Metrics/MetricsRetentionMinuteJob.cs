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
/// Scheduled job that cleans up old minute-level metric data from application databases.
/// </summary>
internal sealed class MetricsRetentionMinuteJob
{
    private readonly ILogger<MetricsRetentionMinuteJob> _logger;
    private readonly MetricsExporterOptions _options;
    private readonly IPlatformDatabaseDiscovery _databaseDiscovery;

    public MetricsRetentionMinuteJob(
        ILogger<MetricsRetentionMinuteJob> logger,
        IOptions<MetricsExporterOptions> options,
        IPlatformDatabaseDiscovery databaseDiscovery)
    {
        _logger = logger;
        _options = options.Value;
        _databaseDiscovery = databaseDiscovery;
    }

    /// <summary>
    /// Executes the retention job for all application databases.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting metrics minute retention job");

        var databases = await _databaseDiscovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.MinuteRetentionDays);

        int totalDeleted = 0;

        foreach (var database in databases)
        {
            try
            {
                var deleted = await DeleteOldMinuteDataAsync(database.ConnectionString, cutoffDate, cancellationToken).ConfigureAwait(false);
                totalDeleted += deleted;

                _logger.LogInformation("Deleted {Count} minute metric rows from database {Database}", deleted, database.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting minute metrics from database {Database}", database.Name);
            }
        }

        _logger.LogInformation("Metrics minute retention job completed. Total deleted: {TotalDeleted}", totalDeleted);
    }

    private static async Task<int> DeleteOldMinuteDataAsync(string connectionString, DateTime cutoffDate, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = """
            DELETE FROM [infra].[MetricPointMinute]
            WHERE BucketStartUtc < @CutoffDate;
            SELECT @@ROWCOUNT;
            """;

        var rowsDeleted = await connection.ExecuteScalarAsync<int>(sql, new { CutoffDate = cutoffDate }).ConfigureAwait(false);
        return rowsDeleted;
    }
}
