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

using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Coordinates fanout operations by acquiring a lease, running the planner, and dispatching slices.
/// Ensures only one coordinator instance is active per fanout topic/work key combination.
/// </summary>
internal sealed class FanoutCoordinator : IFanoutCoordinator
{
    private readonly IFanoutPlanner planner;
    private readonly IFanoutDispatcher dispatcher;
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly ILogger<FanoutCoordinator> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanoutCoordinator"/> class.
    /// </summary>
    /// <param name="planner">The planner to determine which slices are due.</param>
    /// <param name="dispatcher">The dispatcher to enqueue slices for processing.</param>
    /// <param name="leaseFactory">The lease factory for distributed coordination.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    public FanoutCoordinator(
        IFanoutPlanner planner,
        IFanoutDispatcher dispatcher,
        ISystemLeaseFactory leaseFactory,
        ILogger<FanoutCoordinator> logger)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.leaseFactory = leaseFactory ?? throw new ArgumentNullException(nameof(leaseFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<int> RunAsync(string fanoutTopic, string? workKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);

        // 1) Acquire lease to serialize fan-out per topic/workKey
        var leaseName = workKey is null ? $"fanout:{fanoutTopic}" : $"fanout:{fanoutTopic}:{workKey}";
        var contextJson = $"{{\"fanoutTopic\":\"{fanoutTopic}\",\"workKey\":\"{workKey}\",\"machineName\":\"{Environment.MachineName}\"}}";

        var lease = await this.leaseFactory.AcquireAsync(
            leaseName,
            TimeSpan.FromSeconds(90),
            contextJson,
            cancellationToken: ct).ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            if (lease is null)
            {
                this.logger.LogDebug("Could not acquire lease for fanout {FanoutTopic}:{WorkKey}, another instance is already running", fanoutTopic, workKey);
                return 0;
            }

            this.logger.LogInformation("Acquired lease for fanout {FanoutTopic}:{WorkKey}, starting planning pass", fanoutTopic, workKey);

            try
            {
                // 2) Ask planner what's due
                var slices = await this.planner.GetDueSlicesAsync(fanoutTopic, workKey, ct).ConfigureAwait(false);
                if (slices.Count == 0)
                {
                    this.logger.LogDebug("No slices due for fanout {FanoutTopic}:{WorkKey}", fanoutTopic, workKey);
                    return 0;
                }

                this.logger.LogInformation("Found {SliceCount} slices due for fanout {FanoutTopic}:{WorkKey}", slices.Count, fanoutTopic, workKey);

                // 3) Dispatch to messaging system
                var dispatched = await this.dispatcher.DispatchAsync(slices, ct).ConfigureAwait(false);

                this.logger.LogInformation("Successfully dispatched {DispatchedCount} slices for fanout {FanoutTopic}:{WorkKey}", dispatched, fanoutTopic, workKey);
                return dispatched;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error during fanout coordination for {FanoutTopic}:{WorkKey}", fanoutTopic, workKey);
                throw;
            }
        }
    }
}
