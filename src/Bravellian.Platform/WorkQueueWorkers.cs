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
/// Example background worker demonstrating the work queue pattern for outbox processing.
/// </summary>
public sealed class OutboxWorker : BackgroundService
{
    private readonly IOutboxWorkQueue workQueue;
    private readonly ILogger<OutboxWorker> logger;
    private readonly Guid ownerToken = Guid.NewGuid();

    public OutboxWorker(IOutboxWorkQueue workQueue, ILogger<OutboxWorker> logger)
    {
        this.workQueue = workQueue;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int leaseSeconds = 30;
        const int batchSize = 50;
        const int emptyQueueDelayMs = 1000;

        this.logger.LogInformation("OutboxWorker started with owner token {OwnerToken}", this.ownerToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ids = await this.workQueue.ClaimAsync(this.ownerToken, leaseSeconds, batchSize, stoppingToken);
                if (ids.Count == 0)
                {
                    // No items to process, wait with jitter
                    var delay = Random.Shared.Next(emptyQueueDelayMs / 2, emptyQueueDelayMs * 2);
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }

                this.logger.LogDebug("Claimed {Count} outbox items for processing", ids.Count);

                var succeeded = new List<Guid>();
                var failed = new List<Guid>();

                foreach (var id in ids)
                {
                    try
                    {
                        // TODO: Load the actual outbox message and process it
                        // For now, we'll just simulate processing
                        await ProcessOutboxMessageAsync(id, stoppingToken);
                        succeeded.Add(id);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to process outbox message {Id}", id);
                        failed.Add(id);
                    }
                }

                // Acknowledge successful items
                if (succeeded.Count > 0)
                {
                    await this.workQueue.AckAsync(this.ownerToken, succeeded, stoppingToken);
                    this.logger.LogDebug("Acknowledged {Count} outbox items as processed", succeeded.Count);
                }

                // Abandon failed items (they'll be picked up again later)
                if (failed.Count > 0)
                {
                    await this.workQueue.AbandonAsync(this.ownerToken, failed, stoppingToken);
                    this.logger.LogDebug("Abandoned {Count} outbox items for retry", failed.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error in outbox worker loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Backoff on error
            }
        }

        this.logger.LogInformation("OutboxWorker stopped");
    }

    private async Task ProcessOutboxMessageAsync(Guid id, CancellationToken cancellationToken)
    {
        // Simulate processing work
        await Task.Delay(Random.Shared.Next(10, 100), cancellationToken);
        
        // TODO: Replace with actual message processing logic:
        // 1. Load the outbox message from database
        // 2. Send to external system (HTTP API, message bus, etc.)
        // 3. Handle retries and error cases appropriately
        
        this.logger.LogDebug("Processed outbox message {Id}", id);
    }
}

/// <summary>
/// Example background worker demonstrating the work queue pattern for timer processing.
/// </summary>
public sealed class TimerWorker : BackgroundService
{
    private readonly ITimerWorkQueue workQueue;
    private readonly ILogger<TimerWorker> logger;
    private readonly Guid ownerToken = Guid.NewGuid();

    public TimerWorker(ITimerWorkQueue workQueue, ILogger<TimerWorker> logger)
    {
        this.workQueue = workQueue;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int leaseSeconds = 30;
        const int batchSize = 20;
        const int emptyQueueDelayMs = 2000;

        this.logger.LogInformation("TimerWorker started with owner token {OwnerToken}", this.ownerToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ids = await this.workQueue.ClaimAsync(this.ownerToken, leaseSeconds, batchSize, stoppingToken);
                if (ids.Count == 0)
                {
                    // No due timers, wait with jitter
                    var delay = Random.Shared.Next(emptyQueueDelayMs / 2, emptyQueueDelayMs * 2);
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }

                this.logger.LogDebug("Claimed {Count} due timers for processing", ids.Count);

                var succeeded = new List<Guid>();
                var failed = new List<Guid>();

                foreach (var id in ids)
                {
                    try
                    {
                        // TODO: Load the actual timer and process it
                        // For now, we'll just simulate processing
                        await ProcessTimerAsync(id, stoppingToken);
                        succeeded.Add(id);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Failed to process timer {Id}", id);
                        failed.Add(id);
                    }
                }

                // Acknowledge successful items
                if (succeeded.Count > 0)
                {
                    await this.workQueue.AckAsync(this.ownerToken, succeeded, stoppingToken);
                    this.logger.LogDebug("Acknowledged {Count} timers as processed", succeeded.Count);
                }

                // Abandon failed items (they'll be picked up again later)
                if (failed.Count > 0)
                {
                    await this.workQueue.AbandonAsync(this.ownerToken, failed, stoppingToken);
                    this.logger.LogDebug("Abandoned {Count} timers for retry", failed.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error in timer worker loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Backoff on error
            }
        }

        this.logger.LogInformation("TimerWorker stopped");
    }

    private async Task ProcessTimerAsync(Guid id, CancellationToken cancellationToken)
    {
        // Simulate processing work
        await Task.Delay(Random.Shared.Next(50, 200), cancellationToken);
        
        // TODO: Replace with actual timer processing logic:
        // 1. Load the timer from database
        // 2. Execute the timer action (publish message, call webhook, etc.)
        // 3. Handle retries and error cases appropriately
        
        this.logger.LogDebug("Processed timer {Id}", id);
    }
}