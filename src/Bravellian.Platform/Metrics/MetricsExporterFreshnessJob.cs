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


using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Scheduled job that monitors exporter freshness and raises alerts for stale exporters.
/// </summary>
internal sealed class MetricsExporterFreshnessJob
{
    private readonly ILogger<MetricsExporterFreshnessJob> _logger;
    private readonly MetricsExporterOptions _options;

    public MetricsExporterFreshnessJob(
        ILogger<MetricsExporterFreshnessJob> logger,
        IOptions<MetricsExporterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Executes the freshness check.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.CentralConnectionString))
        {
            _logger.LogWarning("Central connection string not configured, skipping freshness check");
            return;
        }

        _logger.LogDebug("Checking metrics exporter freshness");

        try
        {
            var staleExporters = await GetStaleExportersAsync(_options.CentralConnectionString, cancellationToken).ConfigureAwait(false);

            if (staleExporters.Count > 0)
            {
                foreach (var exporter in staleExporters)
                {
                    _logger.LogWarning(
                        "Metrics exporter {InstanceId} is stale. Last flush: {LastFlush}, Age: {AgeMinutes:F1} minutes, Error: {Error}",
                        exporter.InstanceId,
                        exporter.LastFlushUtc,
                        exporter.AgeMinutes,
                        exporter.LastError ?? "(none)");
                }

                _logger.LogError("Found {Count} stale metrics exporters", staleExporters.Count);
            }
            else
            {
                _logger.LogDebug("All metrics exporters are healthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking metrics exporter freshness");
        }
    }

    private static async Task<IReadOnlyList<StaleExporter>> GetStaleExportersAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = """
            SELECT 
                InstanceId,
                LastFlushUtc,
                LastError,
                DATEDIFF(SECOND, LastFlushUtc, SYSUTCDATETIME()) / 60.0 AS AgeMinutes
            FROM [infra].[ExporterHeartbeat]
            WHERE DATEDIFF(SECOND, LastFlushUtc, SYSUTCDATETIME()) > 120
            ORDER BY LastFlushUtc ASC;
            """;

        var results = await connection.QueryAsync<StaleExporter>(sql).ConfigureAwait(false);
        return results.ToList();
    }

    private sealed class StaleExporter
    {
        public string InstanceId { get; set; } = string.Empty;

        public DateTime LastFlushUtc { get; set; }

        public string? LastError { get; set; }

        public double AgeMinutes { get; set; }
    }
}
