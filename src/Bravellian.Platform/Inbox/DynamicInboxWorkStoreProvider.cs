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
/// Provides a mechanism for discovering inbox database configurations dynamically.
/// Implementations can query a registry, database, or configuration service to get
/// the current list of customer databases.
/// </summary>
public interface IInboxDatabaseDiscovery
{
    /// <summary>
    /// Discovers all inbox database configurations that should be processed.
    /// This method is called periodically to detect new or removed databases.
    /// </summary>
    /// <returns>Collection of inbox options for all discovered databases.</returns>
    Task<IEnumerable<InboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a single inbox database.
/// </summary>
public sealed class InboxDatabaseConfig
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
    /// Gets or sets the schema name for the inbox table. Defaults to "dbo".
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the table name for the inbox. Defaults to "Inbox".
    /// </summary>
    public string TableName { get; set; } = "Inbox";

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated when the database is first discovered.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}

/// <summary>
/// Provides access to multiple inbox work stores that are discovered dynamically at runtime.
/// This implementation queries an IInboxDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of inbox work stores accordingly.
/// </summary>
internal sealed class DynamicInboxWorkStoreProvider : IInboxWorkStoreProvider, IDisposable
{
    private readonly IInboxDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicInboxWorkStoreProvider> logger;
    private readonly object lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new(StringComparer.Ordinal);
    private readonly List<IInboxWorkStore> currentStores = new();
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicInboxWorkStoreProvider"/> class.
    /// </summary>
    /// <param name="discovery">The database discovery service.</param>
    /// <param name="timeProvider">Time provider for refresh interval tracking.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    public DynamicInboxWorkStoreProvider(
        IInboxDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<DynamicInboxWorkStoreProvider> logger,
        TimeSpan? refreshInterval = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Asynchronously gets all available inbox work stores that should be processed.
    /// This is the preferred method to avoid potential deadlocks.
    /// </summary>
    /// <returns>A read-only list of inbox work stores to poll.</returns>
    public Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync() =>
        GetAllStoresAsync(CancellationToken.None);

    public async Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync(CancellationToken cancellationToken = default)
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
            // Use semaphore to ensure only one thread performs refresh
            if (await refreshSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
                    lock (lockObject)
                    {
                        lastRefresh = now;
                    }
                }
                finally
                {
                    refreshSemaphore.Release();
                }
            }
        }

        lock (lockObject)
        {
            // Return defensive copy to prevent external mutation
            return currentStores.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetStoreIdentifier(IInboxWorkStore store)
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
    public IInboxWorkStore? GetStoreByKey(string key)
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
    public IInbox? GetInboxByKey(string key)
    {
        lock (lockObject)
        {
            if (storesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Inbox;
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
            // Dispose all stores and inboxes
            foreach (var entry in storesByIdentifier.Values)
            {
                (entry.Store as IDisposable)?.Dispose();
                (entry.Inbox as IDisposable)?.Dispose();
            }

            storesByIdentifier.Clear();
            currentStores.Clear();
        }

        refreshSemaphore?.Dispose();
    }

    private async Task RefreshStoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Discovering inbox databases...");
            var configs = await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<InboxDatabaseConfig>();

            lock (lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>(StringComparer.Ordinal);

                // Update or add stores
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!storesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        logger.LogInformation(
                            "Discovered new inbox database: {Identifier}",
                            config.Identifier);

                        var storeLogger = loggerFactory.CreateLogger<SqlInboxWorkStore>();
                        var store = new SqlInboxWorkStore(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            timeProvider,
                            storeLogger);

                        var inboxLogger = loggerFactory.CreateLogger<SqlInboxService>();
                        var inbox = new SqlInboxService(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            inboxLogger);

                        entry = new StoreEntry
                        {
                            Identifier = config.Identifier,
                            Store = store,
                            Inbox = inbox,
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
                    else if (!string.Equals(entry.Config.ConnectionString, config.ConnectionString, StringComparison.Ordinal) ||
!string.Equals(entry.Config.SchemaName, config.SchemaName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.TableName, config.TableName, StringComparison.Ordinal))
                    {
                        // Configuration changed - recreate the store
                        logger.LogInformation(
                            "Inbox database configuration changed for {Identifier}, recreating store",
                            config.Identifier);

                        currentStores.Remove(entry.Store);

                        // Dispose old instances if they implement IDisposable
                        (entry.Store as IDisposable)?.Dispose();
                        (entry.Inbox as IDisposable)?.Dispose();

                        var storeLogger = loggerFactory.CreateLogger<SqlInboxWorkStore>();
                        var store = new SqlInboxWorkStore(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            timeProvider,
                            storeLogger);

                        var inboxLogger = loggerFactory.CreateLogger<SqlInboxService>();
                        var inbox = new SqlInboxService(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            inboxLogger);

                        entry.Store = store;
                        entry.Inbox = inbox;
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
                        "Inbox database removed: {Identifier}",
                        identifier);

                    var entry = storesByIdentifier[identifier];
                    // Dispose of Store and Inbox if they implement IDisposable
                    (entry.Store as IDisposable)?.Dispose();
                    (entry.Inbox as IDisposable)?.Dispose();

                    // Dispose old instances if they implement IDisposable
                    (entry.Store as IDisposable)?.Dispose();
                    (entry.Inbox as IDisposable)?.Dispose();

                    currentStores.Remove(entry.Store);
                    storesByIdentifier.Remove(identifier);
                }

                logger.LogDebug(
                    "Discovery complete. Managing {Count} inbox databases",
                    storesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    logger.LogInformation(
                        "Deploying inbox schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureInboxSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName,
                        config.TableName).ConfigureAwait(false);

                    await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed inbox schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy inbox schema for database: {Identifier}. Store will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error discovering inbox databases. Continuing with existing configuration.");
        }
    }

    private sealed class StoreEntry
    {
        public required string Identifier { get; set; }

        public required IInboxWorkStore Store { get; set; }

        public required IInbox Inbox { get; set; }

        public required InboxDatabaseConfig Config { get; set; }
    }
}
