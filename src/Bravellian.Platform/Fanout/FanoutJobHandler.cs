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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Outbox handler that processes fanout coordination jobs.
/// This handler receives scheduled fanout jobs and triggers the fanout coordinator.
/// </summary>
internal sealed class FanoutJobHandler : IOutboxHandler
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<FanoutJobHandler> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanoutJobHandler"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving scoped services.</param>
    /// <param name="logger">Logger for this handler.</param>
    public FanoutJobHandler(IServiceProvider serviceProvider, ILogger<FanoutJobHandler> logger)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string Topic => "fanout.coordinate";

    /// <inheritdoc/>
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize the fanout job payload
            var payload = JsonSerializer.Deserialize<FanoutJobPayload>(message.Payload);
            if (payload == null)
            {
                this.logger.LogError("Failed to deserialize fanout job payload for message {MessageId}", message.Id);
                return;
            }

            this.logger.LogDebug(
                "Processing fanout job for topic {FanoutTopic}, workKey {WorkKey}",
                payload.fanoutTopic,
                payload.workKey);

            // Create a scope to resolve the coordinator
            using var scope = this.serviceProvider.CreateScope();

            // Get the coordinator for this topic/workKey combination
            var key = payload.workKey is null ? payload.fanoutTopic : $"{payload.fanoutTopic}:{payload.workKey}";
            var coordinator = scope.ServiceProvider.GetKeyedService<IFanoutCoordinator>(key);

            if (coordinator == null)
            {
                this.logger.LogError("No fanout coordinator found for key {Key}", key);
                return;
            }

            // Run the fanout coordination
            var processedCount = await coordinator.RunAsync(payload.fanoutTopic, payload.workKey, cancellationToken).ConfigureAwait(false);

            this.logger.LogInformation(
                "Fanout coordination completed for {FanoutTopic}:{WorkKey}, processed {Count} slices",
                payload.fanoutTopic,
                payload.workKey,
                processedCount);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error processing fanout job for message {MessageId}", message.Id);
            throw;
        }
    }

    /// <summary>
    /// Payload for fanout coordination jobs.
    /// </summary>
    public sealed record FanoutJobPayload(string fanoutTopic, string? workKey);
}
