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

using System.Collections.Generic;
using System.Linq;
using Cronos;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of ISchedulerStore.
/// Provides scheduler operations for a specific database instance.
/// </summary>
internal sealed class PostgresSchedulerStore : ISchedulerStore
{
    private readonly string connectionString;
    private readonly PostgresSchedulerOptions options;
    private readonly TimeProvider timeProvider;
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}";

    private readonly string jobsTable;
    private readonly string jobRunsTable;
    private readonly string timersTable;
    private readonly string schedulerStateTable;

    private readonly string claimTimersSql;
    private readonly string claimJobsSql;
    private readonly string getNextEventTimeSql;
    private readonly string schedulerStateUpdateSql;

    public PostgresSchedulerStore(IOptions<PostgresSchedulerOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        jobsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobsTableName);
        jobRunsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobRunsTableName);
        timersTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TimersTableName);
        schedulerStateTable = PostgresSqlHelper.Qualify(this.options.SchemaName, "SchedulerState");

        claimTimersSql = $"""
            WITH cte AS (
                SELECT "Id"
                FROM {timersTable}
                WHERE "Status" = 'Pending'
                    AND "DueTime" <= CURRENT_TIMESTAMP
                    AND @FencingToken >= (
                        SELECT COALESCE("CurrentFencingToken", 0)
                        FROM {schedulerStateTable}
                        WHERE "Id" = 1
                    )
                ORDER BY "DueTime"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {timersTable} AS t
            SET "Status" = 'Claimed',
                "ClaimedBy" = @InstanceId,
                "ClaimedAt" = CURRENT_TIMESTAMP
            FROM cte
            WHERE t."Id" = cte."Id"
            RETURNING t."Id", t."Topic", t."Payload";
            """;

        claimJobsSql = $"""
            WITH cte AS (
                SELECT jr."Id", jr."JobId", j."Topic", j."Payload"
                FROM {jobRunsTable} AS jr
                JOIN {jobsTable} AS j ON jr."JobId" = j."Id"
                WHERE jr."Status" = 'Pending'
                    AND jr."ScheduledTime" <= CURRENT_TIMESTAMP
                    AND @FencingToken >= (
                        SELECT COALESCE("CurrentFencingToken", 0)
                        FROM {schedulerStateTable}
                        WHERE "Id" = 1
                    )
                ORDER BY jr."ScheduledTime"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {jobRunsTable} AS jr
            SET "Status" = 'Claimed',
                "ClaimedBy" = @InstanceId,
                "ClaimedAt" = CURRENT_TIMESTAMP
            FROM cte
            WHERE jr."Id" = cte."Id"
            RETURNING jr."Id", jr."JobId", cte."Topic", cte."Payload";
            """;

        getNextEventTimeSql = $"""
            SELECT MIN("NextDue")
            FROM (
                SELECT MIN("DueTime") AS "NextDue" FROM {timersTable} WHERE "Status" = 'Pending'
                UNION ALL
                SELECT MIN("ScheduledTime") AS "NextDue" FROM {jobRunsTable} WHERE "Status" = 'Pending'
                UNION ALL
                SELECT MIN("NextDueTime") AS "NextDue" FROM {jobsTable}
            ) AS "NextEvents";
            """;

        schedulerStateUpdateSql = $"""
            INSERT INTO {schedulerStateTable} ("Id", "CurrentFencingToken", "LastRunAt")
            VALUES (1, @FencingToken, @LastRunAt)
            ON CONFLICT ("Id") DO UPDATE
            SET "CurrentFencingToken" = EXCLUDED."CurrentFencingToken",
                "LastRunAt" = EXCLUDED."LastRunAt"
            WHERE EXCLUDED."CurrentFencingToken" >= {schedulerStateTable}."CurrentFencingToken";
            """;
    }

    public async Task<DateTimeOffset?> GetNextEventTimeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(getNextEventTimeSql).ConfigureAwait(false);
    }

    public async Task<int> CreateJobRunsFromDueJobsAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lease.ThrowIfLost();

            var findDueJobsSql = $"""
                SELECT "Id", "CronSchedule"
                FROM {jobsTable}
                WHERE "NextDueTime" <= @Now;
                """;

            var dueJobs = (await connection.QueryAsync<(Guid Id, string CronSchedule)>(
                findDueJobsSql, new { Now = timeProvider.GetUtcNow() }, transaction).ConfigureAwait(false)).AsList();

            if (!dueJobs.Any())
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }

            lease.ThrowIfLost();

            var runsToInsert = new List<object>(dueJobs.Count);
            var jobsToUpdate = new List<object>(dueJobs.Count);
            var now = timeProvider.GetUtcNow();

            foreach (var job in dueJobs)
            {
                runsToInsert.Add(new
                {
                    RunId = Guid.NewGuid(),
                    JobId = job.Id,
                    ScheduledTime = now,
                });

                var format = job.CronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
                    ? CronFormat.IncludeSeconds
                    : CronFormat.Standard;

                var cronExpression = CronExpression.Parse(job.CronSchedule, format);
                var nextOccurrence = cronExpression.GetNextOccurrence(now.UtcDateTime);
                jobsToUpdate.Add(new
                {
                    NextDueTime = nextOccurrence,
                    JobId = job.Id,
                });
            }

            var insertRunSql = $"""
                INSERT INTO {jobRunsTable} ("Id", "JobId", "ScheduledTime", "Status")
                VALUES (@RunId, @JobId, @ScheduledTime, 'Pending');
                """;

            await connection.ExecuteAsync(insertRunSql, runsToInsert, transaction).ConfigureAwait(false);

            var updateJobSql = $"""
                UPDATE {jobsTable}
                SET "NextDueTime" = @NextDueTime
                WHERE "Id" = @JobId;
                """;

            await connection.ExecuteAsync(updateJobSql, jobsToUpdate, transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return dueJobs.Count;
        }
        catch (LostLeaseException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string Topic, string Payload)>> ClaimDueTimersAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var dueTimers = await connection.QueryAsync<(Guid Id, string Topic, string Payload)>(
            claimTimersSql,
            new { InstanceId = instanceId, FencingToken = lease.FencingToken, BatchSize = batchSize })
            .ConfigureAwait(false);

        return dueTimers.ToList();
    }

    public async Task<IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)>> ClaimDueJobRunsAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var dueJobs = await connection.QueryAsync<(Guid Id, Guid JobId, string Topic, string Payload)>(
            claimJobsSql,
            new { InstanceId = instanceId, FencingToken = lease.FencingToken, BatchSize = batchSize })
            .ConfigureAwait(false);

        return dueJobs.ToList();
    }

    public async Task UpdateSchedulerStateAsync(ISystemLease lease, CancellationToken cancellationToken = default)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(schedulerStateUpdateSql, new
        {
            FencingToken = lease.FencingToken,
            LastRunAt = timeProvider.GetUtcNow(),
        }).ConfigureAwait(false);
    }
}
