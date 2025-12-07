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
/// Background service that periodically processes scheduler work from multiple databases.
/// Each database has its own lease, so multiple instances can run concurrently,
/// each processing different databases.
/// </summary>
public sealed class MultiSchedulerPollingService : BackgroundService
{
    private readonly MultiSchedulerDispatcher dispatcher;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly TimeSpan pollingInterval;
    private readonly ILogger<MultiSchedulerPollingService> logger;

    public MultiSchedulerPollingService(
        MultiSchedulerDispatcher dispatcher,
        ILogger<MultiSchedulerPollingService> logger,
        TimeSpan? pollingInterval = null,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
        this.pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting multi-scheduler polling service with {IntervalSeconds}s interval",
            pollingInterval.TotalSeconds);

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
                // Log and continue - schema deployment errors should not prevent scheduler processing
                logger.LogWarning(ex, "Schema deployment failed, but continuing with scheduler processing");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await dispatcher.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (processedCount > 0)
                {
                    logger.LogDebug(
                        "Multi-scheduler polling iteration completed: {ProcessedCount} items processed",
                        processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Multi-scheduler polling service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let processing errors stop the service
                logger.LogError(ex, "Error in multi-scheduler polling iteration - continuing with next iteration");
            }

            // Sleep until next interval
            await Task.Delay(pollingInterval, stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("Multi-scheduler polling service stopped");
    }
}
