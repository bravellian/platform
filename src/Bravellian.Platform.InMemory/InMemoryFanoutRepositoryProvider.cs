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

internal sealed class InMemoryFanoutRepositoryProvider : IFanoutRepositoryProvider
{
    private readonly IReadOnlyList<IFanoutPolicyRepository> policyRepositories;
    private readonly IReadOnlyList<IFanoutCursorRepository> cursorRepositories;
    private readonly Dictionary<IFanoutPolicyRepository, string> policyIdentifiers;
    private readonly Dictionary<IFanoutCursorRepository, string> cursorIdentifiers;
    private readonly Dictionary<string, IFanoutPolicyRepository> policyByKey;
    private readonly Dictionary<string, IFanoutCursorRepository> cursorByKey;

    public InMemoryFanoutRepositoryProvider(InMemoryPlatformRegistry registry)
    {
        policyRepositories = registry.Stores.Select(store => (IFanoutPolicyRepository)store.FanoutPolicyRepository).ToList();
        cursorRepositories = registry.Stores.Select(store => (IFanoutCursorRepository)store.FanoutCursorRepository).ToList();
        policyIdentifiers = registry.Stores.ToDictionary(store => (IFanoutPolicyRepository)store.FanoutPolicyRepository, store => store.Key);
        cursorIdentifiers = registry.Stores.ToDictionary(store => (IFanoutCursorRepository)store.FanoutCursorRepository, store => store.Key);
        policyByKey = registry.Stores.ToDictionary(store => store.Key, store => (IFanoutPolicyRepository)store.FanoutPolicyRepository, StringComparer.Ordinal);
        cursorByKey = registry.Stores.ToDictionary(store => store.Key, store => (IFanoutCursorRepository)store.FanoutCursorRepository, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<IFanoutPolicyRepository>> GetAllPolicyRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(policyRepositories);
    }

    public Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(cursorRepositories);
    }

    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        return policyIdentifiers.TryGetValue(repository, out var id) ? id : "unknown";
    }

    public string GetRepositoryIdentifier(IFanoutCursorRepository repository)
    {
        return cursorIdentifiers.TryGetValue(repository, out var id) ? id : "unknown";
    }

    public IFanoutPolicyRepository? GetPolicyRepositoryByKey(string key)
    {
        return policyByKey.TryGetValue(key, out var repo) ? repo : null;
    }

    public IFanoutCursorRepository? GetCursorRepositoryByKey(string key)
    {
        return cursorByKey.TryGetValue(key, out var repo) ? repo : null;
    }
}
