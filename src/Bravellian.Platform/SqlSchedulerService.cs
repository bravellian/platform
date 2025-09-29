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
    private readonly ISqlDistributedLock distributedLock;
    private readonly IOutbox outbox;
    private readonly string connectionString;
    private readonly SqlSchedulerOptions options;

    // This is the key tunable parameter.
    private readonly TimeSpan maxWaitTime = TimeSpan.FromSeconds(30);
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}";

    // Pre-built SQL queries using configured table names
    private readonly string claimTimersSql;
    private readonly string claimJobsSql;
    private readonly string getNextEventTimeSql;

    public SqlSchedulerService(ISqlDistributedLock distributedLock, IOutbox outbox, IOptions<SqlSchedulerOptions> options)
    {
        this.distributedLock = distributedLock;
        this.outbox = outbox;
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;

        // Build SQL queries using configured schema and table names
        this.claimTimersSql = $@"
            UPDATE [{this.options.SchemaName}].[{this.options.TimersTableName}]
            SET Status = 'Claimed', ClaimedBy = @InstanceId, ClaimedAt = SYSDATETIMEOFFSET()
            OUTPUT INSERTED.Id, INSERTED.Topic, INSERTED.Payload
            WHERE Id IN (
                SELECT TOP 10 Id FROM [{this.options.SchemaName}].[{this.options.TimersTableName}]
                WHERE Status = 'Pending' AND DueTime <= SYSDATETIMEOFFSET()
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
                ORDER BY ScheduledTime
            );";

        this.getNextEventTimeSql = $@"
            SELECT MIN(NextDue)
            FROM (
                SELECT MIN(DueTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.TimersTableName}] WHERE Status = 'Pending'
                UNION ALL
                SELECT MIN(ScheduledTime) AS NextDue FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] WHERE Status = 'Pending'
            ) AS NextEvents;";
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

            // Use your lock to ensure only one instance acts as the scheduler at a time.
            var handle = await this.distributedLock.AcquireAsync("SchedulerLock", TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                if (handle == null)
                {
                    // Could not get the lock. Another instance is running.
                    // We'll wait a bit before trying again to avoid hammering the DB for the lock.
                    await Task.Delay(this.maxWaitTime, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // LOCK ACQUIRED: We are the active scheduler instance.

                // 1. Process any work that is currently due.
                await this.DispatchDueWorkAsync().ConfigureAwait(false);

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
                    var timeUntilNextEvent = nextEventTime.Value - DateTimeOffset.UtcNow;
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
            } // LOCK IS RELEASED HERE

            // 4. Sleep for the calculated duration.
            if (sleepDuration > TimeSpan.Zero)
            {
                await Task.Delay(sleepDuration, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchDueWorkAsync()
    {
        using (var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            // 1. Start a single transaction for the entire dispatch operation.
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // 2. Process due timers.
                    await this.DispatchTimersAsync(transaction).ConfigureAwait(false);

                    // 3. Process due job runs.
                    await this.DispatchJobRunsAsync(transaction).ConfigureAwait(false);

                    // 4. If all operations succeed, commit the transaction.
                    transaction.Commit();
                }
                catch
                {
                    // If anything fails, the entire operation is rolled back.
                    transaction.Rollback();
                    throw; // Re-throw the exception to be logged by the host.
                }
            }
        }
    }

    private async Task DispatchTimersAsync(Microsoft.Data.SqlClient.SqlTransaction transaction)
    {
        // This SQL query is atomic. It finds pending timers that are due,
        // updates their status to 'Claimed', and immediately returns the data
        // of the rows that it successfully updated. This prevents any race conditions.
        var dueTimers = await transaction.Connection.QueryAsync<(Guid Id, string Topic, string Payload)>(
            this.claimTimersSql, new { InstanceId = this.instanceId }, transaction).ConfigureAwait(false);

        SchedulerMetrics.TimersDispatched.Add(dueTimers.Count());

        foreach (var timer in dueTimers)
        {
            // For each claimed timer, enqueue it into the outbox for a worker to process.
            await this.outbox.EnqueueAsync(
                topic: timer.Topic,
                payload: timer.Payload,
                transaction: transaction,
                correlationId: timer.Id.ToString())
            .ConfigureAwait(false);
        }
    }

    private async Task DispatchJobRunsAsync(Microsoft.Data.SqlClient.SqlTransaction transaction)
    {
        // The logic is identical to timers, just operating on the JobRuns table.
        var dueJobs = await transaction.Connection.QueryAsync<(Guid Id, Guid JobId, string Topic, string Payload)>(
            this.claimJobsSql, new { InstanceId = this.instanceId }, transaction).ConfigureAwait(false);

        SchedulerMetrics.JobsDispatched.Add(dueJobs.Count());

        foreach (var job in dueJobs)
        {
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
        using (var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString))
        {
            return await connection.ExecuteScalarAsync<DateTimeOffset?>(this.getNextEventTimeSql).ConfigureAwait(false);
        }
    }
}
