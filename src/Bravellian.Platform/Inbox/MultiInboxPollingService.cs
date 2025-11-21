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


using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Background service that periodically polls and processes inbox messages from multiple databases.
/// Uses monotonic clock for consistent polling intervals regardless of system time changes.
/// Waits for database schema deployment to complete before starting.
/// </summary>
internal sealed class MultiInboxPollingService : BackgroundService
{
    private readonly MultiInboxDispatcher dispatcher;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly double intervalSeconds;
    private readonly int batchSize;
    private readonly ILogger<MultiInboxPollingService> logger;

    public MultiInboxPollingService(
        MultiInboxDispatcher dispatcher,
        IMonotonicClock mono,
        ILogger<MultiInboxPollingService> logger,
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
        logger.LogInformation(
            "Starting multi-inbox polling service with {IntervalMs}ms interval and batch size {BatchSize}",
            intervalSeconds * 1000, batchSize);

        // Wait for schema deployment to complete if available
        if (schemaCompletion != null)
        {
            logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent inbox processing
                logger.LogWarning(ex, "Schema deployment failed, but continuing with inbox processing");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = mono.Seconds + intervalSeconds;

            try
            {
                var processedCount = await dispatcher.RunOnceAsync(batchSize, stoppingToken).ConfigureAwait(false);
                if (processedCount > 0)
                {
                    logger.LogDebug("Multi-inbox polling iteration completed: {ProcessedCount} messages processed", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Multi-inbox polling service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let processing errors stop the service
                logger.LogError(ex, "Error in multi-inbox polling iteration - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Multi-inbox polling service stopped");
    }
}
