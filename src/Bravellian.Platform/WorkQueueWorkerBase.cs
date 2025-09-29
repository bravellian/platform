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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Base class for background workers that process work queue items.
/// </summary>
/// <typeparam name="TId">The type of the work item identifier.</typeparam>
public abstract class WorkQueueWorkerBase<TId> : BackgroundService
{
    private readonly IWorkQueueClient<TId> workQueueClient;
    private readonly ILogger logger;
    private readonly Guid ownerToken = Guid.NewGuid();

    protected WorkQueueWorkerBase(
        IWorkQueueClient<TId> workQueueClient,
        ILogger logger)
    {
        this.workQueueClient = workQueueClient;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the lease duration in seconds for claimed work items.
    /// </summary>
    protected virtual int LeaseSeconds => 30;

    /// <summary>
    /// Gets the batch size for claiming work items.
    /// </summary>
    protected virtual int BatchSize => 50;

    /// <summary>
    /// Gets the delay between polling cycles when no work is available.
    /// </summary>
    protected virtual TimeSpan PollingDelay => TimeSpan.FromMilliseconds(Random.Shared.Next(200, 600));

    /// <summary>
    /// Gets the delay after an error occurs.
    /// </summary>
    protected virtual TimeSpan ErrorDelay => TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation("Work queue worker started with owner token {OwnerToken}", this.ownerToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimedIds = await this.workQueueClient.ClaimAsync(
                    this.ownerToken,
                    this.LeaseSeconds,
                    this.BatchSize,
                    stoppingToken).ConfigureAwait(false);

                if (claimedIds.Count == 0)
                {
                    // No work available, wait before trying again
                    await Task.Delay(this.PollingDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                this.logger.LogDebug("Claimed {Count} work items", claimedIds.Count);

                var succeededIds = new List<TId>();
                var failedIds = new List<TId>();

                // Process each claimed item
                foreach (var id in claimedIds)
                {
                    try
                    {
                        await this.ProcessWorkItemAsync(id, stoppingToken).ConfigureAwait(false);
                        succeededIds.Add(id);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to process work item {Id}", id);
                        failedIds.Add(id);
                    }
                }

                // Acknowledge successful items
                if (succeededIds.Count > 0)
                {
                    await this.workQueueClient.AckAsync(this.ownerToken, succeededIds, stoppingToken).ConfigureAwait(false);
                    this.logger.LogDebug("Acknowledged {Count} successful work items", succeededIds.Count);
                }

                // Abandon failed items (they will be retried)
                if (failedIds.Count > 0)
                {
                    await this.workQueueClient.AbandonAsync(this.ownerToken, failedIds, stoppingToken).ConfigureAwait(false);
                    this.logger.LogDebug("Abandoned {Count} failed work items for retry", failedIds.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error in work queue processing loop");
                await Task.Delay(this.ErrorDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        this.logger.LogInformation("Work queue worker stopped");
    }

    /// <summary>
    /// Processes a single work item.
    /// </summary>
    /// <param name="id">The identifier of the work item to process.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task ProcessWorkItemAsync(TId id, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the periodic reaper task to handle expired leases.
    /// </summary>
    /// <param name="interval">The interval between reaper runs.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the periodic reaper operation.</returns>
    protected Task StartReaperAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await this.workQueueClient.ReapExpiredAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Error running work queue reaper");
                }

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }
}