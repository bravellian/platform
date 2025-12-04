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


using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Inbox work store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformInboxWorkStoreProvider : IInboxWorkStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformInboxWorkStoreProvider> logger;
    private readonly string tableName;
    private readonly Lock lockObject = new();
    private IReadOnlyList<IInboxWorkStore>? cachedStores;
    private readonly Dictionary<string, IInboxWorkStore> storesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IInbox> inboxesByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new(StringComparer.Ordinal);
    private readonly bool enableSchemaDeployment;
    private readonly PlatformConfiguration? platformConfiguration;

    public PlatformInboxWorkStoreProvider(
        IPlatformDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string tableName,
        bool enableSchemaDeployment = true,
        PlatformConfiguration? platformConfiguration = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PlatformInboxWorkStoreProvider>();
        this.tableName = tableName;
        this.enableSchemaDeployment = enableSchemaDeployment;
        this.platformConfiguration = platformConfiguration;
    }

    public async Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync()
    {
        if (cachedStores == null)
        {
            var databases = (await discovery.DiscoverDatabasesAsync().ConfigureAwait(false)).ToList();

            lock (lockObject)
            {
                if (cachedStores == null)
                {
                    var stores = new List<IInboxWorkStore>();
                    var newDatabases = new List<PlatformDatabase>();

                    foreach (var db in databases)
                    {
                        var options = new SqlInboxOptions
                        {
                            ConnectionString = db.ConnectionString,
                            SchemaName = db.SchemaName,
                            TableName = tableName,
                        };

                        var storeLogger = loggerFactory.CreateLogger<SqlInboxWorkStore>();
                        var store = new SqlInboxWorkStore(
                            Options.Create(options),
                            timeProvider,
                            storeLogger);

                        var inboxLogger = loggerFactory.CreateLogger<SqlInboxService>();
                        var inbox = new SqlInboxService(
                            Options.Create(options),
                            inboxLogger);

                        stores.Add(store);
                        storesByKey[db.Name] = store;
                        inboxesByKey[db.Name] = inbox;

                        // Track new databases for schema deployment
                        if (enableSchemaDeployment && schemasDeployed.TryAdd(db.Name, 0))
                        {
                            newDatabases.Add(db);
                        }
                    }

                    cachedStores = stores;

                    // Deploy schemas for new databases outside the lock
                    if (newDatabases.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (var db in newDatabases)
                            {
                                try
                                {
                                    logger.LogInformation(
                                        "Deploying inbox schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureInboxSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        tableName).ConfigureAwait(false);

                                    await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName).ConfigureAwait(false);

                                    logger.LogInformation(
                                        "Successfully deployed inbox schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(
                                        ex,
                                        "Failed to deploy inbox schema for database: {DatabaseName}. Store may fail on first use.",
                                        db.Name);
                                }
                            }
                        });
                    }
                }
            }
        }

        return cachedStores;
    }

    public string GetStoreIdentifier(IInboxWorkStore store)
    {
        // Find the database name for this store
        foreach (var kvp in storesByKey)
        {
            if (ReferenceEquals(kvp.Value, store))
            {
                return kvp.Key;
            }
        }

        return "unknown";
    }

    public IInboxWorkStore GetStoreByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return storesByKey.TryGetValue(key, out var store)
            ? store
            : throw new KeyNotFoundException($"No inbox work store found for key: {key}");
    }

    public IInbox GetInboxByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return inboxesByKey.TryGetValue(key, out var inbox)
            ? inbox
            : throw new KeyNotFoundException($"No inbox found for key: {key}");
    }
}
