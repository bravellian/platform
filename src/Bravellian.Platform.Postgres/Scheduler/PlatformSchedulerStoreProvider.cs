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


using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Scheduler store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformSchedulerStoreProvider : ISchedulerStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformSchedulerStoreProvider> logger;
    private readonly Lock lockObject = new();
    private IReadOnlyList<ISchedulerStore>? cachedStores;
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new(StringComparer.Ordinal);
    private readonly PlatformConfiguration? platformConfiguration;

    private class StoreEntry
    {
        public required string Identifier { get; init; }
        public required ISchedulerStore Store { get; init; }
        public required ISchedulerClient Client { get; init; }
        public required IOutbox Outbox { get; init; }
    }

    public PlatformSchedulerStoreProvider(
        IPlatformDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        PlatformConfiguration? platformConfiguration = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PlatformSchedulerStoreProvider>();
        this.platformConfiguration = platformConfiguration;
    }

    [SuppressMessage("Performance", "CA1508:Avoid dead conditional code", Justification = "Double-checked locking for cache initialization.")]
    public async Task<IReadOnlyList<ISchedulerStore>> GetAllStoresAsync()
    {
        if (cachedStores == null)
        {
            var databases = (await discovery.DiscoverDatabasesAsync().ConfigureAwait(false)).ToList();

            lock (lockObject)
            {
                if (cachedStores == null)
                {
                    var stores = new List<ISchedulerStore>();

                    foreach (var db in databases)
                    {
                        var store = new PostgresSchedulerStore(
                            Options.Create(new PostgresSchedulerOptions
                            {
                                ConnectionString = db.ConnectionString,
                                SchemaName = db.SchemaName,
                            }),
                            timeProvider);

                        var client = new PostgresSchedulerClient(
                            Options.Create(new PostgresSchedulerOptions
                            {
                                ConnectionString = db.ConnectionString,
                                SchemaName = db.SchemaName,
                            }),
                            timeProvider);

                        var outboxLogger = loggerFactory.CreateLogger<PostgresOutboxService>();
                        var outbox = new PostgresOutboxService(
                            Options.Create(new PostgresOutboxOptions
                            {
                                ConnectionString = db.ConnectionString,
                                SchemaName = db.SchemaName,
                                TableName = "Outbox",
                            }),
                            outboxLogger);

                        var entry = new StoreEntry
                        {
                            Identifier = db.Name,
                            Store = store,
                            Client = client,
                            Outbox = outbox,
                        };

                        storesByIdentifier[db.Name] = entry;
                        stores.Add(store);
                    }

                    cachedStores = stores;
                }
            }
        }

        return cachedStores;
    }

    public string GetStoreIdentifier(ISchedulerStore store)
    {
        // Find the database name for this store
        foreach (var kvp in storesByIdentifier)
        {
            if (ReferenceEquals(kvp.Value.Store, store))
            {
                return kvp.Key;
            }
        }

        return "unknown";
    }

    public ISchedulerStore GetStoreByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return storesByIdentifier.TryGetValue(key, out var entry)
            ? entry.Store
            : throw new KeyNotFoundException($"No scheduler store found for key: {key}");
    }

    public ISchedulerClient GetSchedulerByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return storesByIdentifier.TryGetValue(key, out var entry)
            ? entry.Client
            : throw new KeyNotFoundException($"No scheduler client found for key: {key}");
    }

    public ISchedulerClient GetSchedulerClientByKey(string key)
    {
        // Alias for GetSchedulerByKey
        return GetSchedulerByKey(key);
    }

    public IOutbox GetOutboxByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return storesByIdentifier.TryGetValue(key, out var entry)
            ? entry.Outbox
            : throw new KeyNotFoundException($"No outbox found for key: {key}");
    }
}





