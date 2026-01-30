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
using Bravellian.Platform.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Outbox store provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformOutboxStoreProvider> logger;
    private readonly IPlatformEventEmitter? eventEmitter;
    private readonly string tableName;
    private readonly Lock lockObject = new();
    private IReadOnlyList<IOutboxStore>? cachedStores;
    private readonly Dictionary<string, IOutboxStore> storesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IOutbox> outboxesByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new(StringComparer.Ordinal);
    private readonly bool enableSchemaDeployment;
    private readonly PlatformConfiguration? platformConfiguration;

    public PlatformOutboxStoreProvider(
        IPlatformDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string tableName,
        bool enableSchemaDeployment = true,
        PlatformConfiguration? platformConfiguration = null,
        IPlatformEventEmitter? eventEmitter = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PlatformOutboxStoreProvider>();
        this.tableName = tableName;
        this.enableSchemaDeployment = enableSchemaDeployment;
        this.platformConfiguration = platformConfiguration;
        this.eventEmitter = eventEmitter;
    }

    public async Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync()
    {
        if (cachedStores == null)
        {
            logger.LogDebug("Starting platform database discovery for outbox stores");

            var databases = (await discovery.DiscoverDatabasesAsync().ConfigureAwait(false)).ToList();
            logger.LogDebug(
                "Discovery returned {Count} database(s): {Databases}",
                databases.Count,
                string.Join(", ", databases.Select(FormatDatabase)));

            // If discovery ever returns the control plane DB, surface it loudly but do not remove it
            // (single-DB setups sometimes intentionally share a connection string).
            if (platformConfiguration?.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                !string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneConnectionString))
            {
                foreach (var db in databases)
                {
                    if (IsSameConnection(db.ConnectionString, platformConfiguration.ControlPlaneConnectionString))
                    {
                        logger.LogWarning(
                            "Discovered database {Database} matches the configured control plane connection. " +
                            "Outbox stores should typically exclude the control plane. Check your discovery source.",
                            FormatDatabase(db));
                    }
                }
            }

            lock (lockObject)
            {
                if (cachedStores == null)
                {
                    var stores = new List<IOutboxStore>();
                    var newDatabases = new List<PlatformDatabase>();

                    foreach (var db in databases)
                    {
                        var options = new SqlOutboxOptions
                        {
                            ConnectionString = db.ConnectionString,
                            SchemaName = db.SchemaName,
                            TableName = tableName,
                        };

                        logger.LogDebug(
                            "Creating outbox store for database {Database} (Schema: {Schema}, Catalog: {Catalog})",
                            db.Name,
                            db.SchemaName,
                            TryGetCatalog(db.ConnectionString));

                        var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(options),
                            timeProvider,
                            storeLogger);

                        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(options),
                            outboxLogger,
                            joinStore: null,
                            eventEmitter);

                        stores.Add(store);
                        storesByKey[db.Name] = store;
                        outboxesByKey[db.Name] = outbox;

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
                                        "Deploying outbox schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        tableName).ConfigureAwait(false);

                                    await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName).ConfigureAwait(false);

                                    logger.LogInformation(
                                        "Successfully deployed outbox schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(
                                        ex,
                                        "Failed to deploy outbox schema for database: {DatabaseName}. Store may fail on first use.",
                                        db.Name);
                                }
                            }
                        });
                    }
                }
            }

            RegisterControlPlaneStore();
        }

        return cachedStores;
    }

    public string GetStoreIdentifier(IOutboxStore store)
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

    public IOutboxStore GetStoreByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return storesByKey.TryGetValue(key, out var store)
            ? store
            : throw new KeyNotFoundException($"No outbox store found for key: {key}");
    }

    public IOutbox GetOutboxByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult(); // Initialize stores
        }

        return outboxesByKey.TryGetValue(key, out var outbox)
            ? outbox
            : throw new KeyNotFoundException($"No outbox found for key: {key}");
    }

    private static string FormatDatabase(PlatformDatabase db)
    {
        return $"{db.Name} (Schema: {db.SchemaName}, Catalog: {TryGetCatalog(db.ConnectionString)})";
    }

    private static string TryGetCatalog(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.InitialCatalog)
                ? "<unknown>"
                : builder.InitialCatalog;
        }
        catch
        {
            return "<unparsed>";
        }
    }

    private static bool IsSameConnection(string a, string b)
    {
        try
        {
            var builderA = new SqlConnectionStringBuilder(a);
            var builderB = new SqlConnectionStringBuilder(b);
            return string.Equals(builderA.DataSource, builderB.DataSource, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(builderA.InitialCatalog, builderB.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // If parsing fails, fall back to simple string comparison
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RegisterControlPlaneStore()
    {
        if (platformConfiguration?.EnvironmentStyle != PlatformEnvironmentStyle.MultiDatabaseWithControl ||
            string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneConnectionString))
        {
            return;
        }

        var key = PlatformControlPlaneKeys.ControlPlane;
        if (storesByKey.ContainsKey(key))
        {
            return;
        }

        var schemaName = string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneSchemaName)
            ? "infra"
            : platformConfiguration.ControlPlaneSchemaName;

        var options = new SqlOutboxOptions
        {
            ConnectionString = platformConfiguration.ControlPlaneConnectionString,
            SchemaName = schemaName,
            TableName = tableName,
            EnableSchemaDeployment = platformConfiguration.EnableSchemaDeployment,
        };

        var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
        var store = new SqlOutboxStore(Options.Create(options), timeProvider, storeLogger);

        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
        var outbox = new SqlOutboxService(
            Options.Create(options),
            outboxLogger,
            joinStore: null,
            eventEmitter);

        storesByKey[key] = store;
        outboxesByKey[key] = outbox;
    }
}
