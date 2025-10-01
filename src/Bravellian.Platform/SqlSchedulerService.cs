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

using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

internal class SqlSchedulerService : IHostedService
{
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly IOutbox outbox;
    private readonly string connectionString;
    private readonly SqlSchedulerOptions options;
    private readonly TimeProvider timeProvider;

    // This is the key tunable parameter.
    private readonly TimeSpan maxWaitTime = TimeSpan.FromSeconds(30);
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}";

    // Pre-built SQL queries using configured table names
    private readonly string claimTimersSql;
    private readonly string claimJobsSql;
    private readonly string getNextEventTimeSql;
    private readonly string schedulerStateUpdateSql;

    public SqlSchedulerService(ISystemLeaseFactory leaseFactory, IOutbox outbox, IOptions<SqlSchedulerOptions> options, TimeProvider timeProvider)
    {
        this.leaseFactory = leaseFactory;
        this.outbox = outbox;
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        // Build SQL queries using configured schema and table names
        this.claimTimersSql = $@"
            UPDATE [{this.options.SchemaName}].[{this.options.TimersTableName}]
            SET Status = 'Claimed', ClaimedBy = @InstanceId, ClaimedAt = SYSDATETIMEOFFSET()
            OUTPUT INSERTED.Id, INSERTED.Topic, INSERTED.Payload
            WHERE Id IN (
                SELECT TOP 10 Id FROM [{this.options.SchemaName}].[{this.options.TimersTableName}]
                WHERE Status = 'Pending' AND DueTime <= SYSDATETIMEOFFSET()
                  AND @FencingToken >= (SELECT ISNULL(CurrentFencingToken, 0) FROM [{this.options.SchemaName}].[SchedulerState] WHERE Id = 1)
                ORDER BY DueTime
            );";

        this.claimJobsSql = $@"
            UPDATE [{this.options.SchemaName}].[{this.options.JobRunsTableName}]
            SET Status = 'Claimed', ClaimedBy = @InstanceId, ClaimedAt = SYSDATETIMEOFFSET()
            OUTPUT INSERTED.Id, INSERTED.JobId, j.Topic, j.Payload
            FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] jr
            INNER JOIN [{this.options.SchemaName}].[{this.options.JobsTableName}] j ON jr.JobId = j.Id
            WHERE jr.Id IN (
                SELECT TOP 10 Id FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}]
                WHERE Status = 'Pending' AND ScheduledTime <= SYSDATETIMEOFFSET()
                  AND @FencingToken >= (SELECT ISNULL(CurrentFencingToken, 0) FROM [{this.options.SchemaName}].[SchedulerState] WHERE Id = 1)
                ORDER BY ScheduledTime
            );";

        this.getNextEventTimeSql = $@"
            SELECT MIN(NextDue)
            FROM (
                SELECT MIN(DueTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.TimersTableName}] WHERE Status = 'Pending'
                UNION ALL
                SELECT MIN(ScheduledTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] WHERE Status = 'Pending'
            ) AS NextEvents;";

        // SQL to update the fencing token state for scheduler operations
        this.schedulerStateUpdateSql = $@"
            MERGE [{this.options.SchemaName}].[SchedulerState] AS target
            USING (VALUES (1, @FencingToken, @LastRunAt)) AS source (Id, FencingToken, LastRunAt)
            ON target.Id = source.Id
            WHEN MATCHED AND @FencingToken >= target.CurrentFencingToken THEN
                UPDATE SET CurrentFencingToken = @FencingToken, LastRunAt = @LastRunAt
            WHEN NOT MATCHED THEN
                INSERT (Id, CurrentFencingToken, LastRunAt) VALUES (1, @FencingToken, @LastRunAt);";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(
            async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.SchedulerLoopAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(30_000, cancellationToken).ConfigureAwait(false); // Poll every 30 seconds
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task SchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan sleepDuration;

            // Try to acquire a lease for scheduler processing
            var lease = await this.leaseFactory.AcquireAsync(
                "scheduler:run", 
                TimeSpan.FromSeconds(30), 
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (lease == null)
            {
                // Could not get the lease. Another instance is running.
                // We'll wait a bit before trying again to avoid hammering the DB for the lock.
                await Task.Delay(this.maxWaitTime, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    // LEASE ACQUIRED: We are the active scheduler instance.

                    // Update the fencing state to indicate we're the current scheduler
                    using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    
                    await connection.ExecuteAsync(this.schedulerStateUpdateSql, new 
                    { 
                        FencingToken = lease.FencingToken, 
                        LastRunAt = this.timeProvider.GetUtcNow() 
                    }).ConfigureAwait(false);

                    // 1. Process any work that is currently due.
                    await this.DispatchDueWorkAsync(lease).ConfigureAwait(false);

                    // 2. Find the time of the next scheduled event.
                    var nextEventTime = await this.GetNextEventTimeAsync().ConfigureAwait(false);

                    // 3. Calculate the hybrid sleep duration.
                    if (nextEventTime == null)
                    {
                        // No work is scheduled at all. Sleep for the max wait time.
                        sleepDuration = this.maxWaitTime;
                    }
                    else
                    {
                        var timeUntilNextEvent = nextEventTime.Value - this.timeProvider.GetUtcNow();
                        if (timeUntilNextEvent <= TimeSpan.Zero)
                        {
                            // Work is already due or overdue. Don't sleep.
                            sleepDuration = TimeSpan.Zero;
                        }
                        else
                        {
                            // Sleep until the next event OR max wait time, whichever is shorter.
                            sleepDuration = timeUntilNextEvent < this.maxWaitTime ? timeUntilNextEvent : this.maxWaitTime;
                        }
                    }
                }
                catch (LostLeaseException)
                {
                    // Lease was lost during processing - stop immediately
                    return;
                }
            } // LEASE IS RELEASED HERE

            // 4. Sleep for the calculated duration.
            if (sleepDuration > TimeSpan.Zero)
            {
                await Task.Delay(sleepDuration, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchDueWorkAsync(ISystemLease lease)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // 1. Start a single transaction for the entire dispatch operation.
        using var transaction = connection.BeginTransaction();
        try
        {
            // Check that we still hold the lease before proceeding
            lease.ThrowIfLost();

            // 2. Process due timers.
            await this.DispatchTimersAsync(transaction, lease).ConfigureAwait(false);

            // 3. Process due job runs.
            await this.DispatchJobRunsAsync(transaction, lease).ConfigureAwait(false);

            // 4. If all operations succeed, commit the transaction.
            transaction.Commit();
        }
        catch (LostLeaseException)
        {
            transaction.Rollback();
            throw; // Re-throw lease lost exceptions
        }
        catch
        {
            // If anything fails, the entire operation is rolled back.
            transaction.Rollback();
            throw; // Re-throw the exception to be logged by the host.
        }
    }

    private async Task DispatchTimersAsync(Microsoft.Data.SqlClient.SqlTransaction transaction, ISystemLease lease)
    {
        // This SQL query is atomic. It finds pending timers that are due,
        // updates their status to 'Claimed', and immediately returns the data
        // of the rows that it successfully updated. This prevents any race conditions.
        var dueTimers = await transaction.Connection.QueryAsync<(Guid Id, string Topic, string Payload)>(
            this.claimTimersSql, new { InstanceId = this.instanceId, FencingToken = lease.FencingToken }, transaction).ConfigureAwait(false);

        SchedulerMetrics.TimersDispatched.Add(dueTimers.Count());

        foreach (var timer in dueTimers)
        {
            // Check that we still hold the lease before processing each timer
            lease.ThrowIfLost();

            // For each claimed timer, enqueue it into the outbox for a worker to process.
            await this.outbox.EnqueueAsync(
                topic: timer.Topic,
                payload: timer.Payload,
                transaction: transaction,
                correlationId: timer.Id.ToString())
            .ConfigureAwait(false);
        }
    }

    private async Task DispatchJobRunsAsync(Microsoft.Data.SqlClient.SqlTransaction transaction, ISystemLease lease)
    {
        // The logic is identical to timers, just operating on the JobRuns table.
        var dueJobs = await transaction.Connection.QueryAsync<(Guid Id, Guid JobId, string Topic, string Payload)>(
            this.claimJobsSql, new { InstanceId = this.instanceId, FencingToken = lease.FencingToken }, transaction).ConfigureAwait(false);

        SchedulerMetrics.JobsDispatched.Add(dueJobs.Count());

        foreach (var job in dueJobs)
        {
            // Check that we still hold the lease before processing each job
            lease.ThrowIfLost();

            await this.outbox.EnqueueAsync(
                topic: job.Topic,
                payload: job.Payload, // The payload from the Job definition is passed on.
                transaction: transaction,
                correlationId: job.Id.ToString()) // Correlation is the JobRun Id
            .ConfigureAwait(false);
        }
    }

    private async Task<DateTimeOffset?> GetNextEventTimeAsync()
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString);
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(this.getNextEventTimeSql).ConfigureAwait(false);
    }
}
