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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Background service that processes global scheduler work.
/// </summary>
public sealed class GlobalSchedulerPollingService : BackgroundService
{
    private readonly GlobalSchedulerDispatcher dispatcher;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly TimeSpan pollingInterval;
    private readonly ILogger<GlobalSchedulerPollingService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalSchedulerPollingService"/> class.
    /// </summary>
    /// <param name="dispatcher">Global scheduler dispatcher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="pollingInterval">Optional polling interval override.</param>
    /// <param name="schemaCompletion">Optional schema completion notifier.</param>
    public GlobalSchedulerPollingService(
        GlobalSchedulerDispatcher dispatcher,
        ILogger<GlobalSchedulerPollingService> logger,
        TimeSpan? pollingInterval = null,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
        this.pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Polling loop logs and continues on failures.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting global scheduler polling service with {IntervalSeconds}s interval",
            pollingInterval.TotalSeconds);

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
                logger.LogWarning(ex, "Schema deployment failed, but continuing with global scheduler processing");
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
                        "Global scheduler polling iteration completed: {ProcessedCount} items processed",
                        processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Global scheduler polling service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in global scheduler polling iteration - continuing with next iteration");
            }

            await Task.Delay(pollingInterval, stoppingToken).ConfigureAwait(false);
        }

        logger.LogInformation("Global scheduler polling service stopped");
    }
}
