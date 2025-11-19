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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Scheduler store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformSchedulerStoreProvider : ISchedulerStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformSchedulerStoreProvider> logger;
    private readonly object lockObject = new();
    private IReadOnlyList<ISchedulerStore>? cachedStores;
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new();
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
        this.logger = loggerFactory.CreateLogger<PlatformSchedulerStoreProvider>();
        this.platformConfiguration = platformConfiguration;
    }

    public IReadOnlyList<ISchedulerStore> GetAllStores()
    {
        if (this.cachedStores == null)
        {
            lock (this.lockObject)
            {
                if (this.cachedStores == null)
                {
                    var databases = this.discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult();
                    var stores = new List<ISchedulerStore>();
            
                    foreach (var db in databases)
            {
                // Skip control plane database - it should not have scheduler tables
                if (this.IsControlPlaneDatabase(db))
                {
                    this.logger.LogDebug(
                        "Skipping scheduler store creation for control plane database: {DatabaseName}",
                        db.Name);
                    continue;
                }

                var store = new SqlSchedulerStore(
                    Options.Create(new SqlSchedulerOptions
                    {
                        ConnectionString = db.ConnectionString,
                        SchemaName = db.SchemaName,
                    }),
                    this.timeProvider);

                var client = new SqlSchedulerClient(
                    Options.Create(new SqlSchedulerOptions
                    {
                        ConnectionString = db.ConnectionString,
                        SchemaName = db.SchemaName,
                    }),
                    this.timeProvider);

                var outboxLogger = this.loggerFactory.CreateLogger<SqlOutboxService>();
                var outbox = new SqlOutboxService(
                    Options.Create(new SqlOutboxOptions
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

                    this.storesByIdentifier[db.Name] = entry;
                    stores.Add(store);
                }
            
                this.cachedStores = stores;
                }
            }
        }
        
        return this.cachedStores;
    }

    public string GetStoreIdentifier(ISchedulerStore store)
    {
        // Find the database name for this store
        foreach (var kvp in this.storesByIdentifier)
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
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.storesByIdentifier.TryGetValue(key, out var entry)
            ? entry.Store
            : throw new KeyNotFoundException($"No scheduler store found for key: {key}");
    }

    public ISchedulerClient GetSchedulerByKey(string key)
    {
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.storesByIdentifier.TryGetValue(key, out var entry)
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
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.storesByIdentifier.TryGetValue(key, out var entry)
            ? entry.Outbox
            : throw new KeyNotFoundException($"No outbox found for key: {key}");
    }

    /// <summary>
    /// Checks if the given database is the control plane database by comparing connection strings.
    /// </summary>
    private bool IsControlPlaneDatabase(PlatformDatabase database)
    {
        if (this.platformConfiguration == null || 
            string.IsNullOrEmpty(this.platformConfiguration.ControlPlaneConnectionString))
        {
            return false;
        }

        // Normalize connection strings for comparison
        var dbConnStr = NormalizeConnectionString(database.ConnectionString);
        var cpConnStr = NormalizeConnectionString(this.platformConfiguration.ControlPlaneConnectionString);

        return string.Equals(dbConnStr, cpConnStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a connection string for comparison by removing whitespace and converting to lowercase.
    /// </summary>
    private static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        // Remove all whitespace and convert to lowercase for comparison
        return new string(connectionString.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
    }
}
