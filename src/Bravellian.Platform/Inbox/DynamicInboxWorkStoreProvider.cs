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
}

/// <summary>
/// Provides access to multiple inbox work stores that are discovered dynamically at runtime.
/// This implementation queries an IInboxDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of inbox work stores accordingly.
/// </summary>
public sealed class DynamicInboxWorkStoreProvider : IInboxWorkStoreProvider, IDisposable
{
    private readonly IInboxDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicInboxWorkStoreProvider> logger;
    private readonly object lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new();
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

    /// <inheritdoc/>
    public IReadOnlyList<IInboxWorkStore> GetAllStores()
    {
        // Synchronous version that triggers refresh if needed
        // Note: This uses GetAwaiter().GetResult() which can cause deadlocks in certain contexts.
        // Consider using GetAllStoresAsync when possible.
        return this.GetAllStoresAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously gets all available inbox work stores that should be processed.
    /// This is the preferred method to avoid potential deadlocks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of inbox work stores to poll.</returns>
    public async Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync(CancellationToken cancellationToken = default)
    {
        // Use lock only for updating shared state, not for awaiting
        var now = this.timeProvider.GetUtcNow();
        bool needsRefresh;
        lock (this.lockObject)
        {
            needsRefresh = (now - this.lastRefresh >= this.refreshInterval);
        }

        if (needsRefresh)
        {
            // Use semaphore to ensure only one thread performs refresh
            if (await this.refreshSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await this.RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
                    lock (this.lockObject)
                    {
                        this.lastRefresh = now;
                    }
                }
                finally
                {
                    this.refreshSemaphore.Release();
                }
            }
        }

        lock (this.lockObject)
        {
            // Return defensive copy to prevent external mutation
            return this.currentStores.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetStoreIdentifier(IInboxWorkStore store)
    {
        lock (this.lockObject)
        {
            foreach (var entry in this.storesByIdentifier.Values)
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
        lock (this.lockObject)
        {
            if (this.storesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Store;
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public IInbox? GetInboxByKey(string key)
    {
        lock (this.lockObject)
        {
            if (this.storesByIdentifier.TryGetValue(key, out var entry))
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
        await this.RefreshStoresAsync(cancellationToken).ConfigureAwait(false);
        lock (this.lockObject)
        {
            this.lastRefresh = this.timeProvider.GetUtcNow();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.lockObject)
        {
            // Dispose all stores and inboxes
            foreach (var entry in this.storesByIdentifier.Values)
            {
                (entry.Store as IDisposable)?.Dispose();
                (entry.Inbox as IDisposable)?.Dispose();
            }
            
            this.storesByIdentifier.Clear();
            this.currentStores.Clear();
        }
        
        this.refreshSemaphore?.Dispose();
    }

    private async Task RefreshStoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogDebug("Discovering inbox databases...");
            var configs = await this.discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            lock (this.lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>();

                // Update or add stores
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!this.storesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        this.logger.LogInformation(
                            "Discovered new inbox database: {Identifier}",
                            config.Identifier);

                        var storeLogger = this.loggerFactory.CreateLogger<SqlInboxWorkStore>();
                        var store = new SqlInboxWorkStore(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            storeLogger);

                        var inboxLogger = this.loggerFactory.CreateLogger<SqlInboxService>();
                        var inbox = new SqlInboxService(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            inboxLogger);

                        entry = new StoreEntry
                        {
                            Identifier = config.Identifier,
                            Store = store,
                            Inbox = inbox,
                            Config = config,
                        };

                        this.storesByIdentifier[config.Identifier] = entry;
                        this.currentStores.Add(store);
                    }
                    else if (entry.Config.ConnectionString != config.ConnectionString ||
                             entry.Config.SchemaName != config.SchemaName ||
                             entry.Config.TableName != config.TableName)
                    {
                        // Configuration changed - recreate the store
                        this.logger.LogInformation(
                            "Inbox database configuration changed for {Identifier}, recreating store",
                            config.Identifier);

                        this.currentStores.Remove(entry.Store);

                        // Dispose old instances if they implement IDisposable
                        (entry.Store as IDisposable)?.Dispose();
                        (entry.Inbox as IDisposable)?.Dispose();

                        var storeLogger = this.loggerFactory.CreateLogger<SqlInboxWorkStore>();
                        var store = new SqlInboxWorkStore(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            storeLogger);

                        var inboxLogger = this.loggerFactory.CreateLogger<SqlInboxService>();
                        var inbox = new SqlInboxService(
                            Options.Create(new SqlInboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            inboxLogger);

                        entry.Store = store;
                        entry.Inbox = inbox;
                        entry.Config = config;

                        this.currentStores.Add(store);
                    }
                }

                // Remove stores that are no longer present
                var removedIdentifiers = this.storesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    this.logger.LogInformation(
                        "Inbox database removed: {Identifier}",
                        identifier);

                    var entry = this.storesByIdentifier[identifier];
                    
                    // Dispose old instances if they implement IDisposable
                    (entry.Store as IDisposable)?.Dispose();
                    (entry.Inbox as IDisposable)?.Dispose();
                    
                    this.currentStores.Remove(entry.Store);
                    this.storesByIdentifier.Remove(identifier);
                }

                this.logger.LogDebug(
                    "Discovery complete. Managing {Count} inbox databases",
                    this.storesByIdentifier.Count);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(
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
