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
/// Outbox store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformOutboxStoreProvider> logger;
    private readonly string tableName;
    private readonly object lockObject = new();
    private IReadOnlyList<IOutboxStore>? cachedStores;
    private readonly Dictionary<string, IOutboxStore> storesByKey = new();
    private readonly Dictionary<string, IOutbox> outboxesByKey = new();
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new();
    private readonly bool enableSchemaDeployment;
    private readonly PlatformConfiguration? platformConfiguration;

    public PlatformOutboxStoreProvider(
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
        this.logger = loggerFactory.CreateLogger<PlatformOutboxStoreProvider>();
        this.tableName = tableName;
        this.enableSchemaDeployment = enableSchemaDeployment;
        this.platformConfiguration = platformConfiguration;
    }

    public IReadOnlyList<IOutboxStore> GetAllStores()
    {
        if (this.cachedStores == null)
        {
            lock (this.lockObject)
            {
                if (this.cachedStores == null)
                {
                    var databases = this.discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult();
                    var stores = new List<IOutboxStore>();
                    var newDatabases = new List<PlatformDatabase>();
            
                    foreach (var db in databases)
                    {
                        // Skip control plane database - it should not have outbox tables
                        if (this.IsControlPlaneDatabase(db))
                        {
                            this.logger.LogDebug(
                                "Skipping outbox store creation for control plane database: {DatabaseName}",
                                db.Name);
                            continue;
                        }

                        var options = new SqlOutboxOptions
                        {
                            ConnectionString = db.ConnectionString,
                            SchemaName = db.SchemaName,
                            TableName = this.tableName,
                        };
                        
                        var storeLogger = this.loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(options),
                            this.timeProvider,
                            storeLogger);
                        
                        var outboxLogger = this.loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(options),
                            outboxLogger);
                        
                        stores.Add(store);
                        this.storesByKey[db.Name] = store;
                        this.outboxesByKey[db.Name] = outbox;

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
                                        "Deploying outbox schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        this.tableName).ConfigureAwait(false);

                                    await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName).ConfigureAwait(false);

                                    this.logger.LogInformation(
                                        "Successfully deployed outbox schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    this.logger.LogError(
                                        ex,
                                        "Failed to deploy outbox schema for database: {DatabaseName}. Store may fail on first use.",
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

    public string GetStoreIdentifier(IOutboxStore store)
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

    public IOutboxStore GetStoreByKey(string key)
    {
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.storesByKey.TryGetValue(key, out var store)
            ? store
            : throw new KeyNotFoundException($"No outbox store found for key: {key}");
    }

    public IOutbox GetOutboxByKey(string key)
    {
        if (this.cachedStores == null)
        {
            GetAllStores(); // Initialize stores
        }
        
        return this.outboxesByKey.TryGetValue(key, out var outbox)
            ? outbox
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
