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
}

/// <summary>
/// Provides access to multiple outbox stores that are discovered dynamically at runtime.
/// This implementation queries an IOutboxDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of outbox stores accordingly.
/// </summary>
public sealed class DynamicOutboxStoreProvider : IOutboxStoreProvider, IDisposable
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

    /// <inheritdoc/>
    public IReadOnlyList<IOutboxStore> GetAllStores()
    {
        lock (this.lockObject)
        {
            // Refresh if needed
            var now = this.timeProvider.GetUtcNow();
            if (now - this.lastRefresh >= this.refreshInterval)
            {
                // Refresh asynchronously - we use GetAwaiter().GetResult() here
                // because this is a synchronous interface method.
                // In practice, discovery should be fast (cached, in-memory registry, etc.)
                // ConfigureAwait(false) is used to avoid potential deadlocks.
                this.RefreshStoresAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                this.lastRefresh = now;
            }

            return this.currentStores;
        }
    }

    /// <inheritdoc/>
    public string GetStoreIdentifier(IOutboxStore store)
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
            // Clean up any disposable resources in stores if needed
            this.storesByIdentifier.Clear();
            this.currentStores.Clear();
        }
    }

    private async Task RefreshStoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogDebug("Discovering outbox databases...");
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
                            "Discovered new outbox database: {Identifier}",
                            config.Identifier);

                        var storeLogger = this.loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            this.timeProvider,
                            storeLogger);

                        entry = new StoreEntry
                        {
                            Identifier = config.Identifier,
                            Store = store,
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
                            "Outbox database configuration changed for {Identifier}, recreating store",
                            config.Identifier);

                        this.currentStores.Remove(entry.Store);

                        var storeLogger = this.loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
                            }),
                            this.timeProvider,
                            storeLogger);

                        entry.Store = store;
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
                        "Outbox database removed: {Identifier}",
                        identifier);

                    var entry = this.storesByIdentifier[identifier];
                    this.currentStores.Remove(entry.Store);
                    this.storesByIdentifier.Remove(identifier);
                }

                this.logger.LogDebug(
                    "Discovery complete. Managing {Count} outbox databases",
                    this.storesByIdentifier.Count);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(
                ex,
                "Error discovering outbox databases. Continuing with existing configuration.");
        }
    }

    private sealed class StoreEntry
    {
        public required string Identifier { get; set; }

        public required IOutboxStore Store { get; set; }

        public required OutboxDatabaseConfig Config { get; set; }
    }
}
