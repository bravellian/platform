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

internal sealed class InMemoryInboxCleanupService : BackgroundService
{
    private readonly InMemoryPlatformRegistry registry;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly ILogger<InMemoryInboxCleanupService> logger;

    public InMemoryInboxCleanupService(
        InMemoryPlatformRegistry registry,
        IMonotonicClock mono,
        ILogger<InMemoryInboxCleanupService> logger,
        TimeSpan cleanupInterval)
    {
        this.registry = registry;
        this.mono = mono;
        this.logger = logger;
        this.cleanupInterval = cleanupInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting in-memory inbox cleanup service with cleanup interval {CleanupInterval}",
            cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = mono.Seconds + cleanupInterval.TotalSeconds;

            try
            {
                var totalDeleted = 0;
                foreach (var store in registry.Stores)
                {
                    if (!store.InboxOptions.EnableAutomaticCleanup)
                    {
                        continue;
                    }

                    totalDeleted += store.InboxState.Cleanup(store.InboxOptions.RetentionPeriod);
                }

                if (totalDeleted > 0)
                {
                    logger.LogInformation("In-memory inbox cleanup removed {DeletedCount} message(s)", totalDeleted);
                }
                else
                {
                    logger.LogDebug("In-memory inbox cleanup completed: no messages removed");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during in-memory inbox cleanup");
            }

            while (mono.Seconds < next)
            {
                await Task.Delay(250, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
