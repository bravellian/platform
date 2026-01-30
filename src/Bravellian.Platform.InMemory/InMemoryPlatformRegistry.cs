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

internal sealed class InMemoryPlatformRegistry
{
    private readonly Dictionary<string, InMemoryPlatformStore> storesByKey;

    public InMemoryPlatformRegistry(
        IEnumerable<InMemoryOutboxOptions> outboxOptions,
        IEnumerable<InMemoryInboxOptions> inboxOptions,
        IEnumerable<InMemorySchedulerOptions> schedulerOptions,
        IEnumerable<InMemoryFanoutOptions> fanoutOptions,
        InMemoryOutboxOptions? globalOutboxOptions,
        InMemorySchedulerOptions? globalSchedulerOptions,
        TimeProvider timeProvider)
    {
        var outboxList = outboxOptions.ToList();
        var inboxList = inboxOptions.ToList();
        var schedulerList = schedulerOptions.ToList();
        var fanoutList = fanoutOptions.ToList();

        var keys = outboxList.Select(o => o.StoreKey)
            .Concat(inboxList.Select(o => o.StoreKey))
            .Concat(schedulerList.Select(o => o.StoreKey))
            .Concat(fanoutList.Select(o => o.StoreKey))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one in-memory store must be configured.", nameof(outboxOptions));
        }

        storesByKey = new Dictionary<string, InMemoryPlatformStore>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var outbox = outboxList.FirstOrDefault(o => string.Equals(o.StoreKey, key, StringComparison.Ordinal))
                ?? new InMemoryOutboxOptions { StoreKey = key };
            var inbox = inboxList.FirstOrDefault(o => string.Equals(o.StoreKey, key, StringComparison.Ordinal))
                ?? new InMemoryInboxOptions { StoreKey = key };
            var scheduler = schedulerList.FirstOrDefault(o => string.Equals(o.StoreKey, key, StringComparison.Ordinal))
                ?? new InMemorySchedulerOptions { StoreKey = key };
            var fanout = fanoutList.FirstOrDefault(o => string.Equals(o.StoreKey, key, StringComparison.Ordinal))
                ?? new InMemoryFanoutOptions { StoreKey = key };

            var store = new InMemoryPlatformStore(key, outbox, inbox, scheduler, fanout, timeProvider);
            storesByKey[key] = store;
        }

        Stores = storesByKey.Values.ToList();

        GlobalOutboxOptions = globalOutboxOptions ?? new InMemoryOutboxOptions { StoreKey = "global" };
        GlobalSchedulerOptions = globalSchedulerOptions ?? new InMemorySchedulerOptions { StoreKey = "global" };

        GlobalOutboxState = new InMemoryOutboxState(timeProvider);
        GlobalOutboxJoinStore = new InMemoryOutboxJoinStore(timeProvider);
        GlobalOutboxService = new InMemoryOutboxService(GlobalOutboxState, GlobalOutboxJoinStore);
        GlobalOutboxStore = new InMemoryOutboxStore(GlobalOutboxState, GlobalOutboxJoinStore, GlobalOutboxOptions, timeProvider);

        GlobalSchedulerState = new InMemorySchedulerState(timeProvider);
        GlobalSchedulerClient = new InMemorySchedulerClient(GlobalSchedulerState);
        GlobalSchedulerStore = new InMemorySchedulerStore(GlobalSchedulerState);

        GlobalLeaseFactory = new InMemorySystemLeaseFactory(timeProvider);
    }

    public IReadOnlyList<InMemoryPlatformStore> Stores { get; }

    public InMemoryOutboxOptions GlobalOutboxOptions { get; }

    public InMemorySchedulerOptions GlobalSchedulerOptions { get; }

    public InMemoryOutboxState GlobalOutboxState { get; }

    public InMemoryOutboxJoinStore GlobalOutboxJoinStore { get; }

    public InMemoryOutboxService GlobalOutboxService { get; }

    public InMemoryOutboxStore GlobalOutboxStore { get; }

    public InMemorySchedulerState GlobalSchedulerState { get; }

    public InMemorySchedulerClient GlobalSchedulerClient { get; }

    public InMemorySchedulerStore GlobalSchedulerStore { get; }

    public InMemorySystemLeaseFactory GlobalLeaseFactory { get; }

    public bool TryGetStore(string key, out InMemoryPlatformStore? store)
    {
        return storesByKey.TryGetValue(key, out store);
    }
}
