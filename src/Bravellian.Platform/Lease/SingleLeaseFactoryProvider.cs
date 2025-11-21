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
/// <summary>
/// Simple lease factory provider for single-database setups.
/// </summary>
internal sealed class SingleLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly ISystemLeaseFactory factory;
    private readonly string identifier;
    private readonly IReadOnlyList<ISystemLeaseFactory> factories;

    public SingleLeaseFactoryProvider(ISystemLeaseFactory factory, string identifier)
    {
        this.factory = factory;
        this.identifier = string.IsNullOrWhiteSpace(identifier) ? "default" : identifier;
        factories = new[] { factory };
    }

    public Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(factories);
    }

    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        return ReferenceEquals(factory, this.factory) ? identifier : "unknown";
    }

    public Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.Equals(key, identifier, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ISystemLeaseFactory?>(factory);
        }

        return Task.FromResult<ISystemLeaseFactory?>(null);
    }
}
