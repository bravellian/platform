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
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Bravellian.Platform;

internal sealed class PostgresSchedulerService : BackgroundService
{
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly IOutbox outbox;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly string connectionString;
    private readonly PostgresSchedulerOptions options;
    private readonly TimeProvider timeProvider;

    private readonly TimeSpan maxWaitTime = TimeSpan.FromSeconds(30);
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}";

    private readonly string jobsTable;
    private readonly string jobRunsTable;
    private readonly string timersTable;
    private readonly string schedulerStateTable;

    private readonly string claimTimersSql;
    private readonly string claimJobsSql;
    private readonly string getNextEventTimeSql;
    private readonly string schedulerStateUpdateSql;

    public PostgresSchedulerService(
        ISystemLeaseFactory leaseFactory,
        IOutboxRouter outboxRouter,
        IOutboxStoreProvider outboxStoreProvider,
        PostgresSchedulerOptions options,
        TimeProvider timeProvider,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.leaseFactory = leaseFactory;
        outbox = ResolveOutbox(outboxRouter, outboxStoreProvider);
        this.schemaCompletion = schemaCompletion;
        this.options = options;
        connectionString = options.ConnectionString;
        this.timeProvider = timeProvider;

        jobsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobsTableName);
        jobRunsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobRunsTableName);
        timersTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TimersTableName);
        schedulerStateTable = PostgresSqlHelper.Qualify(this.options.SchemaName, "SchedulerState");

        claimTimersSql = $"""
            WITH cte AS (
                SELECT "Id", "Topic", "Payload"
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
                LIMIT 10
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
                LIMIT 10
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (schemaCompletion != null)
        {
            try
            {
                await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Schema deployment failed, but continuing with scheduler: {ex}");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SchedulerLoopAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(30_000, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SchedulerLoopAsync(CancellationToken cancellationToken)
    {
        TimeSpan sleepDuration;

        var lease = await leaseFactory.AcquireAsync(
            "scheduler:run",
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (lease == null)
        {
            await Task.Delay(maxWaitTime, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await connection.ExecuteAsync(schedulerStateUpdateSql, new
                {
                    FencingToken = lease.FencingToken,
                    LastRunAt = timeProvider.GetUtcNow(),
                }).ConfigureAwait(false);

                await DispatchDueWorkAsync(lease).ConfigureAwait(false);

                var nextEventTime = await GetNextEventTimeAsync().ConfigureAwait(false);

                if (nextEventTime == null)
                {
                    sleepDuration = maxWaitTime;
                }
                else
                {
                    var timeUntilNextEvent = nextEventTime.Value - timeProvider.GetUtcNow();
                    sleepDuration = timeUntilNextEvent <= TimeSpan.Zero
                        ? TimeSpan.Zero
                        : timeUntilNextEvent < maxWaitTime ? timeUntilNextEvent : maxWaitTime;
                }
            }
            catch (LostLeaseException)
            {
                return;
            }
        }

        if (sleepDuration > TimeSpan.Zero)
        {
            await Task.Delay(sleepDuration, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchDueWorkAsync(ISystemLease lease)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            lease.ThrowIfLost();

            await CreateJobRunsFromDueJobsAsync(transaction, lease).ConfigureAwait(false);
            await DispatchTimersAsync(transaction, lease).ConfigureAwait(false);
            await DispatchJobRunsAsync(transaction, lease).ConfigureAwait(false);

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (LostLeaseException)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task CreateJobRunsFromDueJobsAsync(NpgsqlTransaction transaction, ISystemLease lease)
    {
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction has no connection.");
        var findDueJobsSql = $"""
            SELECT "Id", "CronSchedule"
            FROM {jobsTable}
            WHERE "NextDueTime" <= @Now;
            """;

        var dueJobs = (await connection.QueryAsync<(Guid Id, string CronSchedule)>(
            findDueJobsSql, new { Now = timeProvider.GetUtcNow() }, transaction).ConfigureAwait(false)).AsList();

        if (!dueJobs.Any())
        {
            return;
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
    }

    private async Task DispatchTimersAsync(NpgsqlTransaction transaction, ISystemLease lease)
    {
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction has no connection.");
        var dueTimers = await connection.QueryAsync<(Guid Id, string Topic, string Payload)>(
            claimTimersSql,
            new { InstanceId = instanceId, FencingToken = lease.FencingToken },
            transaction).ConfigureAwait(false);

        SchedulerMetrics.TimersDispatched.Add(dueTimers.Count());

        foreach (var timer in dueTimers)
        {
            lease.ThrowIfLost();

            await outbox.EnqueueAsync(
                topic: timer.Topic,
                payload: timer.Payload,
                transaction: transaction,
                correlationId: timer.Id.ToString(),
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
        }
    }

    private async Task DispatchJobRunsAsync(NpgsqlTransaction transaction, ISystemLease lease)
    {
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction has no connection.");
        var dueJobs = await connection.QueryAsync<(Guid Id, Guid JobId, string Topic, string Payload)>(
            claimJobsSql,
            new { InstanceId = instanceId, FencingToken = lease.FencingToken },
            transaction).ConfigureAwait(false);

        SchedulerMetrics.JobsDispatched.Add(dueJobs.Count());

        foreach (var job in dueJobs)
        {
            lease.ThrowIfLost();

            await outbox.EnqueueAsync(
                topic: job.Topic,
                payload: job.Payload ?? string.Empty,
                transaction: transaction,
                correlationId: job.Id.ToString(),
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
        }
    }

    private async Task<DateTimeOffset?> GetNextEventTimeAsync()
    {
        using var connection = new NpgsqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(getNextEventTimeSql).ConfigureAwait(false);
    }

    private static IOutbox ResolveOutbox(IOutboxRouter router, IOutboxStoreProvider storeProvider)
    {
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No outbox stores are configured for the scheduler. Configure at least one store or use the multi-scheduler pipeline.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple outbox stores detected. PostgresSchedulerService supports a single database; use MultiSchedulerDispatcher for multi-database setups.");
        }

        var key = storeProvider.GetStoreIdentifier(stores[0]);
        return router.GetOutbox(key);
    }
}
