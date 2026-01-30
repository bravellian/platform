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

internal sealed class InMemorySchedulerStoreProvider : ISchedulerStoreProvider
{
    private readonly IReadOnlyList<ISchedulerStore> stores;
    private readonly Dictionary<ISchedulerStore, string> identifiers;
    private readonly Dictionary<string, ISchedulerStore> storesByKey;
    private readonly Dictionary<string, ISchedulerClient> clientsByKey;
    private readonly Dictionary<string, IOutbox> outboxesByKey;

    public InMemorySchedulerStoreProvider(InMemoryPlatformRegistry registry)
    {
        stores = registry.Stores.Select(store => (ISchedulerStore)store.SchedulerStore).ToList();
        identifiers = registry.Stores.ToDictionary(store => (ISchedulerStore)store.SchedulerStore, store => store.Key);
        storesByKey = registry.Stores.ToDictionary(store => store.Key, store => (ISchedulerStore)store.SchedulerStore, StringComparer.Ordinal);
        clientsByKey = registry.Stores.ToDictionary(store => store.Key, store => (ISchedulerClient)store.SchedulerClient, StringComparer.Ordinal);
        outboxesByKey = registry.Stores.ToDictionary(store => store.Key, store => (IOutbox)store.OutboxService, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<ISchedulerStore>> GetAllStoresAsync()
    {
        return Task.FromResult(stores);
    }

    public string GetStoreIdentifier(ISchedulerStore store)
    {
        return identifiers.TryGetValue(store, out var id) ? id : "unknown";
    }

    public ISchedulerStore? GetStoreByKey(string key)
    {
        return storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    public ISchedulerClient? GetSchedulerClientByKey(string key)
    {
        return clientsByKey.TryGetValue(key, out var client) ? client : null;
    }

    public IOutbox? GetOutboxByKey(string key)
    {
        return outboxesByKey.TryGetValue(key, out var outbox) ? outbox : null;
    }
}
