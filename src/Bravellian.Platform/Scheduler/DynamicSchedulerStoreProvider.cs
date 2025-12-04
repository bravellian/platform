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
/// Provides access to multiple scheduler stores that are discovered dynamically at runtime.
/// This implementation queries an ISchedulerDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of scheduler stores accordingly.
/// </summary>
public sealed class DynamicSchedulerStoreProvider : ISchedulerStoreProvider, IDisposable
{
    private readonly ISchedulerDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicSchedulerStoreProvider> logger;
    private readonly Lock lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new(StringComparer.Ordinal);
    private readonly List<ISchedulerStore> currentStores = new();
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicSchedulerStoreProvider"/> class.
    /// </summary>
    /// <param name="discovery">The database discovery service.</param>
    /// <param name="timeProvider">Time provider for refresh interval tracking.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    public DynamicSchedulerStoreProvider(
        ISchedulerDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<DynamicSchedulerStoreProvider> logger,
        TimeSpan? refreshInterval = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Asynchronously gets all available scheduler stores that should be processed.
    /// This is the preferred method to avoid potential deadlocks.
    /// </summary>
    /// <returns>A read-only list of scheduler stores to poll.</returns>
    public Task<IReadOnlyList<ISchedulerStore>> GetAllStoresAsync() =>
        GetAllStoresAsync(CancellationToken.None);

    public async Task<IReadOnlyList<ISchedulerStore>> GetAllStoresAsync(CancellationToken cancellationToken = default)
    {
        // Use lock only for updating shared state, not for awaiting
        var now = timeProvider.GetUtcNow();
        bool needsRefresh;
        lock (lockObject)
        {
            needsRefresh = (now - lastRefresh >= refreshInterval);
        }

        // Use semaphore to ensure only one thread performs refresh
        if (needsRefresh && await refreshSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
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

        lock (lockObject)
        {
            // Return defensive copy to prevent external mutation
            return currentStores.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetStoreIdentifier(ISchedulerStore store)
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
    public ISchedulerStore? GetStoreByKey(string key)
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
    public ISchedulerClient? GetSchedulerClientByKey(string key)
    {
        lock (lockObject)
        {
            if (storesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Client;
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

        refreshSemaphore?.Dispose();
    }

    private async Task RefreshStoresAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Discovering scheduler databases...");
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
                            "Discovered new scheduler database: {Identifier}",
                            config.Identifier);

                        var store = new SqlSchedulerStore(
                            Options.Create(new SqlSchedulerOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                JobsTableName = config.JobsTableName,
                                JobRunsTableName = config.JobRunsTableName,
                                TimersTableName = config.TimersTableName,
                            }),
                            timeProvider);

                        var client = new SqlSchedulerClient(
                            Options.Create(new SqlSchedulerOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                JobsTableName = config.JobsTableName,
                                JobRunsTableName = config.JobRunsTableName,
                                TimersTableName = config.TimersTableName,
                            }),
                            timeProvider);

                        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = "Outbox",
                            }),
                            outboxLogger);

                        entry = new StoreEntry
                        {
                            Identifier = config.Identifier,
                            Store = store,
                            Client = client,
                            Outbox = outbox,
                            Config = config,
                        };

                        storesByIdentifier[config.Identifier] = entry;
                        currentStores.Add(store);
                    }
                    else if (!string.Equals(entry.Config.ConnectionString, config.ConnectionString, StringComparison.Ordinal) ||
!string.Equals(entry.Config.SchemaName, config.SchemaName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.JobsTableName, config.JobsTableName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.JobRunsTableName, config.JobRunsTableName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.TimersTableName, config.TimersTableName, StringComparison.Ordinal))
                    {
                        // Configuration changed - recreate the store
                        logger.LogInformation(
                            "Scheduler database configuration changed for {Identifier}, recreating store",
                            config.Identifier);

                        currentStores.Remove(entry.Store);

                        var store = new SqlSchedulerStore(
                            Options.Create(new SqlSchedulerOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                JobsTableName = config.JobsTableName,
                                JobRunsTableName = config.JobRunsTableName,
                                TimersTableName = config.TimersTableName,
                            }),
                            timeProvider);

                        var client = new SqlSchedulerClient(
                            Options.Create(new SqlSchedulerOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                JobsTableName = config.JobsTableName,
                                JobRunsTableName = config.JobRunsTableName,
                                TimersTableName = config.TimersTableName,
                            }),
                            timeProvider);

                        var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
                        var outbox = new SqlOutboxService(
                            Options.Create(new SqlOutboxOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                TableName = "Outbox",
                            }),
                            outboxLogger);

                        entry.Store = store;
                        entry.Client = client;
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
                        "Scheduler database removed: {Identifier}",
                        identifier);

                    var entry = storesByIdentifier[identifier];
                    currentStores.Remove(entry.Store);
                    storesByIdentifier.Remove(identifier);
                }

                logger.LogDebug(
                    "Discovery complete. Managing {Count} scheduler databases",
                    storesByIdentifier.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error discovering scheduler databases. Continuing with existing configuration.");
        }
    }

    private sealed class StoreEntry
    {
        public required string Identifier { get; set; }

        public required ISchedulerStore Store { get; set; }

        public required ISchedulerClient Client { get; set; }

        public required IOutbox Outbox { get; set; }

        public required SchedulerDatabaseConfig Config { get; set; }
    }
}
