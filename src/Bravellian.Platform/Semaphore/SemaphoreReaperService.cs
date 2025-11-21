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
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Semaphore;
/// <summary>
/// Background service that periodically reaps expired semaphore leases.
/// </summary>
internal sealed class SemaphoreReaperService : BackgroundService
{
    private readonly ISemaphoreService semaphoreService;
    private readonly SemaphoreOptions options;
    private readonly ILogger<SemaphoreReaperService> logger;

    public SemaphoreReaperService(
        ISemaphoreService semaphoreService,
        IOptions<SemaphoreOptions> options,
        ILogger<SemaphoreReaperService> logger)
    {
        this.semaphoreService = semaphoreService;
        this.options = options.Value;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Semaphore reaper service starting with cadence {CadenceSeconds}s and batch size {BatchSize}",
            options.ReaperCadenceSeconds,
            options.ReaperBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.ReaperCadenceSeconds),
                    stoppingToken).ConfigureAwait(false);

                var deletedCount = await semaphoreService.ReapExpiredAsync(
                    name: null,
                    maxRows: options.ReaperBatchSize,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                if (deletedCount > 0)
                {
                    logger.LogInformation("Semaphore reaper deleted {DeletedCount} expired leases", deletedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Semaphore reaper encountered an error");
            }
        }

        logger.LogInformation("Semaphore reaper service stopped");
    }
}
