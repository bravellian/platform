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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal class SchedulerHealthCheck : IHealthCheck
{
    private readonly string connectionString;
    private readonly TimeProvider timeProvider;
    private readonly string schemaName;
    private readonly string timersTableName;
    private readonly string jobRunsTableName;

    public SchedulerHealthCheck(IOptions<SqlSchedulerOptions> options, TimeProvider timeProvider)
    {
        var schedulerOptions = options.Value;
        connectionString = schedulerOptions.ConnectionString;
        schemaName = schedulerOptions.SchemaName;
        timersTableName = schedulerOptions.TimersTableName;
        jobRunsTableName = schedulerOptions.JobRunsTableName;
        this.timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // This query is designed to be very fast and non-locking.
            var sql = $"""
                SELECT
                    (SELECT MIN(CreatedAt) FROM [{schemaName}].[Outbox] WITH(NOLOCK) WHERE IsProcessed = 0) AS OldestOutbox,
                    (SELECT MIN(DueTime) FROM [{schemaName}].[{timersTableName}] WITH(NOLOCK) WHERE Status = 'Pending') AS OldestTimer,
                    (SELECT MIN(ScheduledTime) FROM [{schemaName}].[{jobRunsTableName}] WITH(NOLOCK) WHERE Status = 'Pending') AS OldestJobRun;
                """;

            using (var connection = new SqlConnection(connectionString))
            {
                var result = await connection.QuerySingleAsync(sql).ConfigureAwait(false);
                DateTimeOffset? oldestItem = Min(result.OldestOutbox, result.OldestTimer, result.OldestJobRun);

                if (oldestItem == null)
                {
                    return HealthCheckResult.Healthy("No pending items.");
                }

                var age = timeProvider.GetUtcNow() - oldestItem.Value;

                // Define thresholds for system health
                if (age > TimeSpan.FromHours(1))
                {
                    return HealthCheckResult.Unhealthy($"Processing is stalled. Oldest pending item is {age.TotalMinutes:F0} minutes old.");
                }

                if (age > TimeSpan.FromMinutes(10))
                {
                    return HealthCheckResult.Degraded($"Processing is delayed. Oldest pending item is {age.TotalMinutes:F0} minutes old.");
                }

                return HealthCheckResult.Healthy($"Oldest pending item is {age.TotalSeconds:F0} seconds old.");
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to connect to or query the scheduler database.", ex);
        }
    }

    private DateTimeOffset? Min(params DateTimeOffset?[] dates)
    {
        DateTimeOffset? minDate = null;
        foreach (var date in dates)
        {
            if (date.HasValue && (!minDate.HasValue || date.Value < minDate.Value))
            {
                minDate = date.Value;
            }
        }

        return minDate;
    }
}
