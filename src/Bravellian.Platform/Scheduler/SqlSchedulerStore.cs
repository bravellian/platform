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

namespace Bravellian.Platform;

using Cronos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

/// <summary>
/// SQL Server implementation of ISchedulerStore.
/// Provides scheduler operations for a specific database instance.
/// </summary>
internal sealed class SqlSchedulerStore : ISchedulerStore
{
    private readonly string connectionString;
    private readonly SqlSchedulerOptions options;
    private readonly TimeProvider timeProvider;
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}";

    // Pre-built SQL queries using configured table names
    private readonly string claimTimersSql;
    private readonly string claimJobsSql;
    private readonly string getNextEventTimeSql;
    private readonly string schedulerStateUpdateSql;

    public SqlSchedulerStore(IOptions<SqlSchedulerOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        // Build SQL queries using configured schema and table names
        this.claimTimersSql = $"""

                        UPDATE [{this.options.SchemaName}].[{this.options.TimersTableName}]
                        SET Status = 'Claimed', ClaimedBy = @InstanceId, ClaimedAt = SYSDATETIMEOFFSET()
                        OUTPUT INSERTED.Id, INSERTED.Topic, INSERTED.Payload
                        WHERE Id IN (
                            SELECT TOP (@BatchSize) Id FROM [{this.options.SchemaName}].[{this.options.TimersTableName}]
                            WHERE Status = 'Pending' AND DueTime <= SYSDATETIMEOFFSET()
                              AND @FencingToken >= (SELECT ISNULL(CurrentFencingToken, 0) FROM [{this.options.SchemaName}].[SchedulerState] WHERE Id = 1)
                            ORDER BY DueTime
                        );
            """;

        this.claimJobsSql = $"""

                        UPDATE [{this.options.SchemaName}].[{this.options.JobRunsTableName}]
                        SET Status = 'Claimed', ClaimedBy = @InstanceId, ClaimedAt = SYSDATETIMEOFFSET()
                        OUTPUT INSERTED.Id, INSERTED.JobId, j.Topic, j.Payload
                        FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] jr
                        INNER JOIN [{this.options.SchemaName}].[{this.options.JobsTableName}] j ON jr.JobId = j.Id
                        WHERE jr.Id IN (
                            SELECT TOP (@BatchSize) Id FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}]
                            WHERE Status = 'Pending' AND ScheduledTime <= SYSDATETIMEOFFSET()
                              AND @FencingToken >= (SELECT ISNULL(CurrentFencingToken, 0) FROM [{this.options.SchemaName}].[SchedulerState] WHERE Id = 1)
                            ORDER BY ScheduledTime
                        );
            """;

        this.getNextEventTimeSql = $"""

                        SELECT MIN(NextDue)
                        FROM (
                            SELECT MIN(DueTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.TimersTableName}] WHERE Status = 'Pending'
                            UNION ALL
                            SELECT MIN(ScheduledTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] WHERE Status = 'Pending'
                            UNION ALL
                            SELECT MIN(NextDueTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.JobsTableName}]
                        ) AS NextEvents;
            """;

        // SQL to update the fencing token state for scheduler operations
        this.schedulerStateUpdateSql = $"""

                        MERGE [{this.options.SchemaName}].[SchedulerState] AS target
                        USING (VALUES (1, @FencingToken, @LastRunAt)) AS source (Id, FencingToken, LastRunAt)
                        ON target.Id = source.Id
                        WHEN MATCHED AND @FencingToken >= target.CurrentFencingToken THEN
                            UPDATE SET CurrentFencingToken = @FencingToken, LastRunAt = @LastRunAt
                        WHEN NOT MATCHED THEN
                            INSERT (Id, CurrentFencingToken, LastRunAt) VALUES (1, @FencingToken, @LastRunAt);
            """;
    }

    public async Task<DateTimeOffset?> GetNextEventTimeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(this.connectionString);
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(this.getNextEventTimeSql).ConfigureAwait(false);
    }

    public async Task<int> CreateJobRunsFromDueJobsAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            lease.ThrowIfLost();

            var findDueJobsSql = $"""

                        SELECT Id, CronSchedule FROM [{this.options.SchemaName}].[{this.options.JobsTableName}]
                        WHERE NextDueTime <= @Now;
            """;

            var dueJobs = (await transaction.Connection.QueryAsync<(Guid Id, string CronSchedule)>(
                findDueJobsSql, new { Now = this.timeProvider.GetUtcNow() }, transaction).ConfigureAwait(false)).AsList();

            if (!dueJobs.Any())
            {
                transaction.Commit();
                return 0;
            }

            lease.ThrowIfLost();

            var runsToInsert = new List<object>();
            var jobsToUpdate = new List<object>();
            var now = this.timeProvider.GetUtcNow();

            foreach (var job in dueJobs)
            {
                // Prepare the new JobRun record
                runsToInsert.Add(new
                {
                    RunId = Guid.NewGuid(),
                    JobId = job.Id,
                    ScheduledTime = now,
                });

                // Determine cron format based on the number of parts.
                var format = job.CronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
                    ? CronFormat.IncludeSeconds
                    : CronFormat.Standard;

                // Calculate the next occurrence and prepare the update
                var cronExpression = CronExpression.Parse(job.CronSchedule, format);
                var nextOccurrence = cronExpression.GetNextOccurrence(now.UtcDateTime);
                jobsToUpdate.Add(new
                {
                    NextDueTime = nextOccurrence,
                    JobId = job.Id,
                });
            }

            var insertRunSql = $"""

                        INSERT INTO [{this.options.SchemaName}].[{this.options.JobRunsTableName}] (Id, JobId, ScheduledTime, Status)
                        VALUES (@RunId, @JobId, @ScheduledTime, 'Pending');
            """;

            await transaction.Connection.ExecuteAsync(insertRunSql, runsToInsert, transaction).ConfigureAwait(false);

            var updateJobSql = $"""

                        UPDATE [{this.options.SchemaName}].[{this.options.JobsTableName}]
                        SET NextDueTime = @NextDueTime
                        WHERE Id = @JobId;
            """;

            await transaction.Connection.ExecuteAsync(updateJobSql, jobsToUpdate, transaction).ConfigureAwait(false);

            transaction.Commit();
            return dueJobs.Count;
        }
        catch (LostLeaseException)
        {
            transaction.Rollback();
            throw;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string Topic, string Payload)>> ClaimDueTimersAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var dueTimers = await connection.QueryAsync<(Guid Id, string Topic, string Payload)>(
            this.claimTimersSql,
            new { InstanceId = this.instanceId, FencingToken = lease.FencingToken, BatchSize = batchSize })
            .ConfigureAwait(false);

        return dueTimers.ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)>> ClaimDueJobRunsAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var dueJobs = await connection.QueryAsync<(Guid Id, Guid JobId, string Topic, string Payload)>(
            this.claimJobsSql,
            new { InstanceId = this.instanceId, FencingToken = lease.FencingToken, BatchSize = batchSize })
            .ConfigureAwait(false);

        return dueJobs.ToList();
    }

    public async Task UpdateSchedulerStateAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(this.schedulerStateUpdateSql, new
        {
            FencingToken = lease.FencingToken,
            LastRunAt = this.timeProvider.GetUtcNow(),
        }).ConfigureAwait(false);
    }
}
