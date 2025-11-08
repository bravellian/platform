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
/// Background service that periodically polls and processes inbox messages.
/// Uses monotonic clock for consistent polling intervals regardless of system time changes.
/// Waits for database schema deployment to complete before starting.
/// Mirrors the OutboxPollingService implementation.
/// </summary>
internal sealed class InboxPollingService : BackgroundService
{
    private readonly InboxDispatcher dispatcher;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly double intervalSeconds;
    private readonly int batchSize;
    private readonly ILogger<InboxPollingService> logger;

    public InboxPollingService(
        InboxDispatcher dispatcher,
        IMonotonicClock mono,
        ILogger<InboxPollingService> logger,
        double intervalSeconds = 0.25, // 250ms default
        int batchSize = 50,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.dispatcher = dispatcher;
        this.mono = mono;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
        this.intervalSeconds = intervalSeconds;
        this.batchSize = batchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation(
            "Inbox polling service starting with {IntervalMs}ms interval and batch size {BatchSize}",
            this.intervalSeconds * 1000, this.batchSize);

        // Wait for schema deployment to complete
        if (this.schemaCompletion != null)
        {
            this.logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await this.schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                this.logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent inbox processing
                this.logger.LogWarning(ex, "Schema deployment failed, but continuing with inbox processing");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = this.mono.Seconds + this.intervalSeconds;

            try
            {
                var processedCount = await this.dispatcher.RunOnceAsync(this.batchSize, stoppingToken).ConfigureAwait(false);
                if (processedCount > 0)
                {
                    this.logger.LogDebug("Inbox polling iteration completed: {ProcessedCount} messages processed", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                this.logger.LogDebug("Inbox polling service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let processing errors stop the service
                this.logger.LogError(ex, "Error in inbox polling iteration - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - this.mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        this.logger.LogInformation("Inbox polling service stopped");
    }
}
