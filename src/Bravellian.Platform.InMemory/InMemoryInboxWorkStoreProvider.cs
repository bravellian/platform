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

internal sealed class InMemoryInboxWorkStoreProvider : IInboxWorkStoreProvider
{
    private readonly IReadOnlyList<IInboxWorkStore> stores;
    private readonly Dictionary<IInboxWorkStore, string> identifiers;
    private readonly Dictionary<string, IInboxWorkStore> storesByKey;
    private readonly Dictionary<string, IInbox> inboxesByKey;

    public InMemoryInboxWorkStoreProvider(InMemoryPlatformRegistry registry)
    {
        stores = registry.Stores.Select(store => (IInboxWorkStore)store.InboxWorkStore).ToList();
        identifiers = registry.Stores.ToDictionary(store => (IInboxWorkStore)store.InboxWorkStore, store => store.Key);
        storesByKey = registry.Stores.ToDictionary(store => store.Key, store => (IInboxWorkStore)store.InboxWorkStore, StringComparer.Ordinal);
        inboxesByKey = registry.Stores.ToDictionary(store => store.Key, store => (IInbox)store.InboxService, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync()
    {
        return Task.FromResult(stores);
    }

    public string GetStoreIdentifier(IInboxWorkStore store)
    {
        return identifiers.TryGetValue(store, out var id) ? id : "unknown";
    }

    public IInboxWorkStore? GetStoreByKey(string key)
    {
        return storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    public IInbox? GetInboxByKey(string key)
    {
        return inboxesByKey.TryGetValue(key, out var inbox) ? inbox : null;
    }
}
