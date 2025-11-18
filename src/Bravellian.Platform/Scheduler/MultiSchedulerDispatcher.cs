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

using Microsoft.Extensions.Logging;

/// <summary>
/// Dispatches scheduler work across multiple databases/tenants using a pluggable
/// selection strategy to determine which scheduler to process next.
/// This enables processing scheduler work from multiple customer databases in a single worker.
/// </summary>
public sealed class MultiSchedulerDispatcher
{
    private readonly ISchedulerStoreProvider storeProvider;
    private readonly IOutboxSelectionStrategy selectionStrategy;
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MultiSchedulerDispatcher> logger;

    private ISchedulerStore? lastProcessedStore;
    private int lastProcessedCount;

    public MultiSchedulerDispatcher(
        ISchedulerStoreProvider storeProvider,
        IOutboxSelectionStrategy selectionStrategy,
        ISystemLeaseFactory leaseFactory,
        TimeProvider timeProvider,
        ILogger<MultiSchedulerDispatcher> logger)
    {
        this.storeProvider = storeProvider;
        this.selectionStrategy = selectionStrategy;
        this.leaseFactory = leaseFactory;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Processes scheduler work from the next selected store.
    /// Uses the selection strategy to determine which scheduler to process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of work items processed.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var stores = this.storeProvider.GetAllStores();

        if (stores.Count == 0)
        {
            this.logger.LogDebug("No scheduler stores available for processing");
            return 0;
        }

        // Use the selection strategy to pick the next store
        // Note: The selection strategy is designed for IOutboxStore, so we fall back to round-robin
        // since we cannot properly cast scheduler stores to outbox stores
        var selectedStore = this.lastProcessedStore;
        
        if (selectedStore == null || stores.Count == 1)
        {
            // First time or only one store - pick the first one
            selectedStore = stores[0];
        }
        else
        {
            // Round-robin through stores for now
            // TODO: Create an ISelectionStrategy<T> to properly support pluggable strategies
            var storesList = stores as List<ISchedulerStore> ?? stores.ToList();
            var index = storesList.IndexOf(this.lastProcessedStore!);
            selectedStore = storesList[(index + 1) % storesList.Count];
        }

        var storeIdentifier = this.storeProvider.GetStoreIdentifier(selectedStore);
        this.logger.LogDebug(
            "Processing scheduler work from store '{StoreIdentifier}'",
            storeIdentifier);

        var processedCount = await this.ProcessStoreAsync(selectedStore, storeIdentifier, cancellationToken)
            .ConfigureAwait(false);

        this.lastProcessedStore = selectedStore;
        this.lastProcessedCount = processedCount;

        return processedCount;
    }

    private async Task<int> ProcessStoreAsync(
        ISchedulerStore store,
        string storeIdentifier,
        CancellationToken cancellationToken)
    {
        // Try to acquire a lease for scheduler processing for this specific database
        var leaseKey = $"scheduler:run:{storeIdentifier}";
        var lease = await this.leaseFactory.AcquireAsync(
            leaseKey,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (lease == null)
        {
            // Could not get the lease. Another instance is processing this database.
            this.logger.LogDebug(
                "Could not acquire lease for scheduler store '{StoreIdentifier}'. Another instance may be processing it.",
                storeIdentifier);
            return 0;
        }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                // Update the scheduler state with the fencing token
                await store.UpdateSchedulerStateAsync(lease, cancellationToken).ConfigureAwait(false);

                var totalProcessed = 0;

                // 1. Create job runs from any due job definitions
                lease.ThrowIfLost();
                var jobRunsCreated = await store.CreateJobRunsFromDueJobsAsync(lease, cancellationToken)
                    .ConfigureAwait(false);
                if (jobRunsCreated > 0)
                {
                    this.logger.LogInformation(
                        "Created {Count} job runs for store '{StoreIdentifier}'",
                        jobRunsCreated,
                        storeIdentifier);
                }

                // 2. Get the outbox for this database
                var outbox = this.storeProvider.GetOutboxByKey(storeIdentifier);
                if (outbox == null)
                {
                    this.logger.LogWarning(
                        "No outbox found for store '{StoreIdentifier}'. Cannot dispatch scheduler work.",
                        storeIdentifier);
                    return 0;
                }

                // 3. Process due timers
                lease.ThrowIfLost();
                var dueTimers = await store.ClaimDueTimersAsync(lease, 10, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var timer in dueTimers)
                {
                    lease.ThrowIfLost();
                    await outbox.EnqueueAsync(
                        topic: timer.Topic,
                        payload: timer.Payload,
                        correlationId: timer.Id.ToString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                    totalProcessed++;
                }

                if (dueTimers.Count > 0)
                {
                    this.logger.LogInformation(
                        "Dispatched {Count} timers for store '{StoreIdentifier}'",
                        dueTimers.Count,
                        storeIdentifier);
                    SchedulerMetrics.TimersDispatched.Add(dueTimers.Count);
                }

                // 4. Process due job runs
                lease.ThrowIfLost();
                var dueJobs = await store.ClaimDueJobRunsAsync(lease, 10, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var job in dueJobs)
                {
                    lease.ThrowIfLost();
                    await outbox.EnqueueAsync(
                        topic: job.Topic,
                        payload: job.Payload ?? string.Empty,
                        correlationId: job.Id.ToString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                    totalProcessed++;
                }

                if (dueJobs.Count > 0)
                {
                    this.logger.LogInformation(
                        "Dispatched {Count} job runs for store '{StoreIdentifier}'",
                        dueJobs.Count,
                        storeIdentifier);
                    SchedulerMetrics.JobsDispatched.Add(dueJobs.Count);
                }

                return totalProcessed;
            }
            catch (LostLeaseException)
            {
                // Lease was lost during processing - stop immediately
                this.logger.LogWarning(
                    "Lost lease while processing scheduler store '{StoreIdentifier}'",
                    storeIdentifier);
                return 0;
            }
            catch (Exception ex)
            {
                this.logger.LogError(
                    ex,
                    "Error processing scheduler store '{StoreIdentifier}'",
                    storeIdentifier);
                return 0;
            }
        }
    }
}
