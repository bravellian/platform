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


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Provides a mechanism for discovering outbox database configurations dynamically.
/// Implementations can query a registry, database, or configuration service to get
/// the current list of customer databases.
/// </summary>
public interface IOutboxDatabaseDiscovery
{
    /// <summary>
    /// Discovers all outbox database configurations that should be processed.
    /// This method is called periodically to detect new or removed databases.
    /// </summary>
    /// <returns>Collection of outbox options for all discovered databases.</returns>
    Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a single outbox database.
/// </summary>
public sealed class OutboxDatabaseConfig
{
    /// <summary>
    /// Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
    /// </summary>
    public required string Identifier { get; set; }

    /// <summary>
    /// Gets or sets the database connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the schema name for the outbox table. Defaults to "dbo".
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the table name for the outbox. Defaults to "Outbox".
    /// </summary>
    public string TableName { get; set; } = "Outbox";

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated when the database is first discovered.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}

/// <summary>
/// Provides access to multiple outbox stores that are discovered dynamically at runtime.
/// This implementation queries an IOutboxDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of outbox stores accordingly.
/// </summary>
internal sealed class DynamicOutboxStoreProvider : IOutboxStoreProvider, IDisposable
{
    private readonly IOutboxDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicOutboxStoreProvider> logger;
    private readonly object lockObject = new();
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new();
    private readonly List<IOutboxStore> currentStores = new();
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicOutboxStoreProvider"/> class.
    /// </summary>
    /// <param name="discovery">The database discovery service.</param>
    /// <param name="timeProvider">Time provider for refresh interval tracking.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    public DynamicOutboxStoreProvider(
        IOutboxDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<DynamicOutboxStoreProvider> logger,
        TimeSpan? refreshInterval = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Asynchronously gets all available outbox stores that should be processed.
    /// This is the preferred method to avoid potential deadlocks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of outbox stores to poll.</returns>
    public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync() =>
        GetAllStoresAsync(CancellationToken.None);

    public async Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync(CancellationToken cancellationToken = default)
    {
        // Use lock only for updating shared state, not for awaiting
        var now = timeProvider.GetUtcNow();
        bool needsRefresh;
        lock (lockObject)
        {
            needsRefresh = (now - lastRefresh >= refreshInterval);
        }
        if (needsRefresh)
        {
            await RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
            lock (lockObject)
            {
                lastRefresh = now;
            }
        }
        lock (lockObject)
        {
            return currentStores;
        }
    }

    /// <inheritdoc/>
    public string GetStoreIdentifier(IOutboxStore store)
    {
        lock (lockObject)
        {
            foreach (var entry in storesByIdentifier.Values)
            {
                if (ReferenceEquals(entry.Store, store))
                {
                    return entry.Identifier;
                }
            }

            return "Unknown";
        }
    }

    /// <inheritdoc/>
    public IOutboxStore? GetStoreByKey(string key)
    {
        lock (lockObject)
        {
            if (storesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Store;
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public IOutbox? GetOutboxByKey(string key)
    {
        lock (lockObject)
        {
            if (storesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Outbox;
            }

            return null;
        }
    }

    /// <summary>
    /// Forces an immediate refresh of the database list.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
        lock (lockObject)
        {
            lastRefresh = timeProvider.GetUtcNow();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (lockObject)
        {
            // Clean up any disposable resources in stores if needed
            storesByIdentifier.Clear();
            currentStores.Clear();
        }
    }

    private async Task RefreshStoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Discovering outbox databases...");
            var configs = await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<OutboxDatabaseConfig>();

            lock (lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>();

                // Update or add stores
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!storesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        logger.LogInformation(
                            "Discovered new outbox database: {Identifier}",
                            config.Identifier);

                        var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            timeProvider,
                            storeLogger);

                        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            outboxLogger);

                        entry = new StoreEntry
                        {
                            Identifier = config.Identifier,
                            Store = store,
                            Outbox = outbox,
                            Config = config,
                        };

                        storesByIdentifier[config.Identifier] = entry;
                        currentStores.Add(store);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                    else if (entry.Config.ConnectionString != config.ConnectionString ||
                             entry.Config.SchemaName != config.SchemaName ||
                             entry.Config.TableName != config.TableName)
                    {
                        // Configuration changed - recreate the store
                        logger.LogInformation(
                            "Outbox database configuration changed for {Identifier}, recreating store",
                            config.Identifier);

                        currentStores.Remove(entry.Store);

                        var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            timeProvider,
                            storeLogger);

                        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            outboxLogger);

                        entry.Store = store;
                        entry.Outbox = outbox;
                        entry.Config = config;

                        currentStores.Add(store);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                }

                // Remove stores that are no longer present
                var removedIdentifiers = storesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    logger.LogInformation(
                        "Outbox database removed: {Identifier}",
                        identifier);

                    var entry = storesByIdentifier[identifier];
                    currentStores.Remove(entry.Store);
                    storesByIdentifier.Remove(identifier);
                }

                logger.LogDebug(
                    "Discovery complete. Managing {Count} outbox databases",
                    storesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    logger.LogInformation(
                        "Deploying outbox schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName,
                        config.TableName).ConfigureAwait(false);

                    await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed outbox schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy outbox schema for database: {Identifier}. Store will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error discovering outbox databases. Continuing with existing configuration.");
        }
    }

    private sealed class StoreEntry
    {
        public required string Identifier { get; set; }

        public required IOutboxStore Store { get; set; }

        public required IOutbox Outbox { get; set; }

        public required OutboxDatabaseConfig Config { get; set; }
    }
}
