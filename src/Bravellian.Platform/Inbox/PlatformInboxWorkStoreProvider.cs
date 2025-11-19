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

using System.Collections.Concurrent;
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
    private readonly ILogger<PlatformInboxWorkStoreProvider> logger;
    private readonly string tableName;
    private readonly object lockObject = new();
    private IReadOnlyList<IInboxWorkStore>? cachedStores;
    private readonly Dictionary<string, IInboxWorkStore> storesByKey = new();
    private readonly Dictionary<string, IInbox> inboxesByKey = new();
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new();
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
        this.logger = loggerFactory.CreateLogger<PlatformInboxWorkStoreProvider>();
        this.tableName = tableName;
        this.enableSchemaDeployment = enableSchemaDeployment;
        this.platformConfiguration = platformConfiguration;
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
                    var newDatabases = new List<PlatformDatabase>();
            
                    foreach (var db in databases)
                    {
                        // Skip control plane database - it should not have inbox tables
                        if (this.IsControlPlaneDatabase(db))
                        {
                            this.logger.LogDebug(
                                "Skipping inbox store creation for control plane database: {DatabaseName}",
                                db.Name);
                            continue;
                        }

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

                        // Track new databases for schema deployment
                        if (this.enableSchemaDeployment && this.schemasDeployed.TryAdd(db.Name, 0))
                        {
                            newDatabases.Add(db);
                        }
                    }
            
                    this.cachedStores = stores;

                    // Deploy schemas for new databases outside the lock
                    if (newDatabases.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (var db in newDatabases)
                            {
                                try
                                {
                                    this.logger.LogInformation(
                                        "Deploying inbox schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureInboxSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        this.tableName).ConfigureAwait(false);

                                    await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName).ConfigureAwait(false);

                                    this.logger.LogInformation(
                                        "Successfully deployed inbox schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    this.logger.LogError(
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
