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
/// Default implementation of <see cref="ILeaseRouter"/> that routes to tenant-specific lease factories.
/// </summary>
internal sealed class LeaseRouter : ILeaseRouter
{
    private readonly ILeaseFactoryProvider leaseFactoryProvider;
    private readonly ILogger<LeaseRouter> logger;

    public LeaseRouter(ILeaseFactoryProvider leaseFactoryProvider, ILogger<LeaseRouter> logger)
    {
        this.leaseFactoryProvider = leaseFactoryProvider ?? throw new ArgumentNullException(nameof(leaseFactoryProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ISystemLeaseFactory> GetLeaseFactoryAsync(string routingKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
        {
            return await GetDefaultLeaseFactoryAsync(cancellationToken).ConfigureAwait(false);
        }

        var factory = await leaseFactoryProvider.GetFactoryByKeyAsync(routingKey, cancellationToken).ConfigureAwait(false);
        if (factory != null)
        {
            return factory;
        }

        var factories = await leaseFactoryProvider.GetAllFactoriesAsync(cancellationToken).ConfigureAwait(false);
        if (factories.Count == 0)
        {
            throw new InvalidOperationException("No lease factories are configured. AddSystemLeases or AddMultiSystemLeases must be registered.");
        }

        if (factories.Count == 1)
        {
            logger.LogDebug(
                "Routing key '{RoutingKey}' not found. Falling back to the single configured lease factory '{Identifier}'.",
                routingKey,
                leaseFactoryProvider.GetFactoryIdentifier(factories[0]));
            return factories[0];
        }

        throw new KeyNotFoundException(
            $"No lease factory found for key '{routingKey}'. Configure a matching tenant or provide the correct routing key.");
    }

    public async Task<ISystemLeaseFactory> GetDefaultLeaseFactoryAsync(CancellationToken cancellationToken = default)
    {
        var factories = await leaseFactoryProvider.GetAllFactoriesAsync(cancellationToken).ConfigureAwait(false);
        if (factories.Count == 0)
        {
            throw new InvalidOperationException("No lease factories are configured. AddSystemLeases or AddMultiSystemLeases must be registered.");
        }

        if (factories.Count > 1)
        {
            logger.LogWarning(
                "Multiple lease factories are configured. Defaulting to the first ('{Identifier}'). Supply a routing key to target a specific tenant.",
                leaseFactoryProvider.GetFactoryIdentifier(factories[0]));
        }

        return factories[0];
    }
}
