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
/// Hosted service that executes registered startup checks during application startup.
/// </summary>
public sealed class StartupCheckRunnerHostedService : IHostedService
{
    private readonly IReadOnlyList<IStartupCheck> checks;
    private readonly IStartupLatch latch;
    private readonly ILogger<StartupCheckRunnerHostedService> logger;
    private readonly IHostApplicationLifetime? hostApplicationLifetime;
    private Task? runTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupCheckRunnerHostedService"/> class.
    /// </summary>
    /// <param name="checks">The startup checks to run.</param>
    /// <param name="latch">The startup latch for tracking in-progress checks.</param>
    /// <param name="logger">The logger.</param>
    public StartupCheckRunnerHostedService(
        IEnumerable<IStartupCheck> checks,
        IStartupLatch latch,
        ILogger<StartupCheckRunnerHostedService> logger,
        IHostApplicationLifetime? hostApplicationLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(checks);
        this.checks = checks.ToList();
        this.latch = latch ?? throw new ArgumentNullException(nameof(latch));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.hostApplicationLifetime = hostApplicationLifetime;
    }

    /// <summary>
    /// Gets a task that completes when startup checks finish running.
    /// </summary>
    public Task Completion => runTask ?? Task.CompletedTask;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var orderedChecks = checks
            .OrderBy(check => check.Order)
            .ThenBy(check => check.Name, StringComparer.Ordinal)
            .ToArray();

        StartupCheckValidator.ValidateUniqueNames(orderedChecks);

        runTask = RunChecksAsync(orderedChecks, cancellationToken);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (runTask is null)
        {
            return;
        }

        await Task.WhenAny(runTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
    }

    private async Task RunChecksAsync(IEnumerable<IStartupCheck> orderedChecks, CancellationToken cancellationToken)
    {
        foreach (var check in orderedChecks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var step = latch.Register($"startup-check:{check.Name}");
            logger.LogInformation("Starting startup check {Name}", check.Name);

            try
            {
                await check.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Startup check {Name} OK", check.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup check {Name} failed", check.Name);

                if (check.IsCritical)
                {
                    hostApplicationLifetime?.StopApplication();
                    throw;
                }

                logger.LogWarning(
                    "Startup check {Name} failed but is non-critical; continuing startup.",
                    check.Name);
            }
        }
    }
}
