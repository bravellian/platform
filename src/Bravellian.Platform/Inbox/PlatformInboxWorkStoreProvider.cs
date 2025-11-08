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
/// Inbox work store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformInboxWorkStoreProvider : IInboxWorkStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly string tableName;
    private readonly object lockObject = new();
    private IReadOnlyList<IInboxWorkStore>? cachedStores;
    private readonly Dictionary<string, IInboxWorkStore> storesByKey = new();
    private readonly Dictionary<string, IInbox> inboxesByKey = new();

    public PlatformInboxWorkStoreProvider(
        IPlatformDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string tableName)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.tableName = tableName;
    }

    public IReadOnlyList<IInboxWorkStore> GetAllStores()
    {
        if (this.cachedStores == null)
        {
            lock (this.lockObject)
            {
                if (this.cachedStores == null)
                {
                    var databases = this.discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult();
                    var stores = new List<IInboxWorkStore>();
            
                    foreach (var db in databases)
            {
                var options = new SqlInboxOptions
                {
                    ConnectionString = db.ConnectionString,
                    SchemaName = db.SchemaName,
                    TableName = this.tableName,
                };
                
                var storeLogger = this.loggerFactory.CreateLogger<SqlInboxWorkStore>();
                var store = new SqlInboxWorkStore(
                    Options.Create(options),
                    storeLogger);
                
                var inboxLogger = this.loggerFactory.CreateLogger<SqlInboxService>();
                var inbox = new SqlInboxService(
                    Options.Create(options),
                    inboxLogger);
                
                    stores.Add(store);
                    this.storesByKey[db.Name] = store;
                    this.inboxesByKey[db.Name] = inbox;
                }
            
                this.cachedStores = stores;
                }
            }
        }
        
        return this.cachedStores;
    }

    public string GetStoreIdentifier(IInboxWorkStore store)
    {
        // Find the database name for this store
        foreach (var kvp in this.storesByKey)
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
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.storesByKey.TryGetValue(key, out var store)
            ? store
            : throw new KeyNotFoundException($"No inbox work store found for key: {key}");
    }

    public IInbox GetInboxByKey(string key)
    {
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.inboxesByKey.TryGetValue(key, out var inbox)
            ? inbox
            : throw new KeyNotFoundException($"No inbox found for key: {key}");
    }
}
