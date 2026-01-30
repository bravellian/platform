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

using System.Linq;

namespace Bravellian.Platform;

internal sealed class InMemoryLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly IReadOnlyList<ISystemLeaseFactory> factories;
    private readonly Dictionary<ISystemLeaseFactory, string> identifiers;
    private readonly Dictionary<string, ISystemLeaseFactory> factoriesByKey;

    public InMemoryLeaseFactoryProvider(InMemoryPlatformRegistry registry)
    {
        factories = registry.Stores.Select(store => (ISystemLeaseFactory)store.LeaseFactory).ToList();
        identifiers = registry.Stores.ToDictionary(store => (ISystemLeaseFactory)store.LeaseFactory, store => store.Key);
        factoriesByKey = registry.Stores.ToDictionary(store => store.Key, store => (ISystemLeaseFactory)store.LeaseFactory, StringComparer.Ordinal);

        identifiers[registry.GlobalLeaseFactory] = PlatformControlPlaneKeys.ControlPlane;
        factoriesByKey[PlatformControlPlaneKeys.ControlPlane] = registry.GlobalLeaseFactory;
    }

    public Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(factories);
    }

    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        return identifiers.TryGetValue(factory, out var id) ? id : "unknown";
    }

    public Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(factoriesByKey.TryGetValue(key, out var factory) ? factory : null);
    }
}
