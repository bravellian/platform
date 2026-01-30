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

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Dispatches scheduler work for the global scheduler.
/// </summary>
public sealed class GlobalSchedulerDispatcher
{
    private const string LeaseKey = "scheduler:run:global";
    private readonly IGlobalSchedulerStore store;
    private readonly IGlobalOutbox outbox;
    private readonly IGlobalSystemLeaseFactory leaseFactory;
    private readonly ILogger<GlobalSchedulerDispatcher> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalSchedulerDispatcher"/> class.
    /// </summary>
    /// <param name="store">Global scheduler store.</param>
    /// <param name="outbox">Global outbox instance.</param>
    /// <param name="leaseFactory">Lease factory for global scheduler coordination.</param>
    /// <param name="logger">Logger instance.</param>
    public GlobalSchedulerDispatcher(
        IGlobalSchedulerStore store,
        IGlobalOutbox outbox,
        IGlobalSystemLeaseFactory leaseFactory,
        ILogger<GlobalSchedulerDispatcher> logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        this.leaseFactory = leaseFactory ?? throw new ArgumentNullException(nameof(leaseFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes scheduler work for the global scheduler once.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of work items processed.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Dispatcher logs and continues on failures.")]
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var lease = await leaseFactory.AcquireAsync(
            LeaseKey,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (lease == null)
        {
            logger.LogDebug("Global scheduler lease is held by another instance");
            return 0;
        }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                await store.UpdateSchedulerStateAsync(lease, cancellationToken).ConfigureAwait(false);

                var totalProcessed = 0;

                lease.ThrowIfLost();
                var jobRunsCreated = await store.CreateJobRunsFromDueJobsAsync(lease, cancellationToken)
                    .ConfigureAwait(false);

                if (jobRunsCreated > 0)
                {
                    logger.LogInformation("Global scheduler created {Count} job runs", jobRunsCreated);
                }

                lease.ThrowIfLost();
                var dueTimers = await store.ClaimDueTimersAsync(lease, 10, cancellationToken).ConfigureAwait(false);
                foreach (var timer in dueTimers)
                {
                    lease.ThrowIfLost();
                    await outbox.EnqueueAsync(
                        topic: timer.Topic,
                        payload: timer.Payload,
                        correlationId: timer.Id.ToString(),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    totalProcessed++;
                }

                if (dueTimers.Count > 0)
                {
                    SchedulerMetrics.TimersDispatched.Add(dueTimers.Count);
                }

                lease.ThrowIfLost();
                var dueJobs = await store.ClaimDueJobRunsAsync(lease, 10, cancellationToken).ConfigureAwait(false);
                foreach (var job in dueJobs)
                {
                    lease.ThrowIfLost();
                    await outbox.EnqueueAsync(
                        topic: job.Topic,
                        payload: job.Payload ?? string.Empty,
                        correlationId: job.Id.ToString(),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    totalProcessed++;
                }

                if (dueJobs.Count > 0)
                {
                    SchedulerMetrics.JobsDispatched.Add(dueJobs.Count);
                }

                return totalProcessed;
            }
            catch (LostLeaseException)
            {
                logger.LogWarning("Lost global scheduler lease during processing");
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing global scheduler work");
                return 0;
            }
        }
    }
}
