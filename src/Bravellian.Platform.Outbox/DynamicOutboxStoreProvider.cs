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
    private readonly Lock lockObject = new();
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new(StringComparer.Ordinal);
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
                            "Discovered new outbox database: {Identifier}",
                            config.Identifier);

                        var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
                        var store = new SqlOutboxStore(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = config.TableName,
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
                    }
                    else if (!string.Equals(entry.Config.ConnectionString, config.ConnectionString, StringComparison.Ordinal) ||
!string.Equals(entry.Config.SchemaName, config.SchemaName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.TableName, config.TableName, StringComparison.Ordinal))
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
                            }),
                            outboxLogger);

                        entry.Store = store;
                        entry.Outbox = outbox;
                        entry.Config = config;

                        currentStores.Add(store);
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
