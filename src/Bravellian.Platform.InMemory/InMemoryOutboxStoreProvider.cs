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

internal sealed class InMemoryOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IReadOnlyList<IOutboxStore> stores;
    private readonly Dictionary<IOutboxStore, string> identifiers;
    private readonly Dictionary<string, IOutboxStore> storesByKey;
    private readonly Dictionary<string, IOutbox> outboxesByKey;

    public InMemoryOutboxStoreProvider(InMemoryPlatformRegistry registry)
    {
        stores = registry.Stores.Select(store => (IOutboxStore)store.OutboxStore).ToList();
        identifiers = registry.Stores.ToDictionary(store => (IOutboxStore)store.OutboxStore, store => store.Key);
        storesByKey = registry.Stores.ToDictionary(store => store.Key, store => (IOutboxStore)store.OutboxStore, StringComparer.Ordinal);
        outboxesByKey = registry.Stores.ToDictionary(store => store.Key, store => (IOutbox)store.OutboxService, StringComparer.Ordinal);

        identifiers[registry.GlobalOutboxStore] = PlatformControlPlaneKeys.ControlPlane;
        storesByKey[PlatformControlPlaneKeys.ControlPlane] = registry.GlobalOutboxStore;
        outboxesByKey[PlatformControlPlaneKeys.ControlPlane] = registry.GlobalOutboxService;
    }

    public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync()
    {
        return Task.FromResult(stores);
    }

    public string GetStoreIdentifier(IOutboxStore store)
    {
        return identifiers.TryGetValue(store, out var id) ? id : "unknown";
    }

    public IOutboxStore? GetStoreByKey(string key)
    {
        return storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    public IOutbox? GetOutboxByKey(string key)
    {
        return outboxesByKey.TryGetValue(key, out var outbox) ? outbox : null;
    }
}
