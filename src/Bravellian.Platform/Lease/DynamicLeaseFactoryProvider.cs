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

namespace Bravellian.Platform;
/// <summary>
/// Provides access to multiple lease factories that are discovered dynamically at runtime.
/// This implementation queries an ILeaseDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of lease factories accordingly.
/// </summary>
internal sealed class DynamicLeaseFactoryProvider : ILeaseFactoryProvider, IDisposable
{
    private readonly ILeaseDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicLeaseFactoryProvider> logger;
    private readonly object lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, FactoryEntry> factoriesByIdentifier = new();
    private readonly List<ISystemLeaseFactory> currentFactories = new();
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicLeaseFactoryProvider"/> class.
    /// </summary>
    /// <param name="discovery">The database discovery service.</param>
    /// <param name="timeProvider">Time provider for refresh interval tracking.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    public DynamicLeaseFactoryProvider(
        ILeaseDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<DynamicLeaseFactoryProvider> logger,
        TimeSpan? refreshInterval = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISystemLeaseFactory> GetAllFactories()
    {
        // Synchronous version that triggers refresh if needed
        // Note: This uses GetAwaiter().GetResult() which can cause deadlocks in certain contexts.
        // Consider using GetAllFactoriesAsync when possible.
        return GetAllFactoriesAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously gets all available lease factories that should be managed.
    /// This is the preferred method to avoid potential deadlocks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of lease factories to manage.</returns>
    public async Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default)
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
                await RefreshFactoriesAsync(cancellationToken).ConfigureAwait(false);
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
            return currentFactories.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        lock (lockObject)
        {
            foreach (var entry in factoriesByIdentifier.Values)
            {
                if (ReferenceEquals(entry.Factory, factory))
                {
                    return entry.Identifier;
                }
            }

            return "Unknown";
        }
    }

    /// <inheritdoc/>
    public Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (lockObject)
        {
            if (factoriesByIdentifier.TryGetValue(key, out var entry))
            {
                return Task.FromResult<ISystemLeaseFactory?>(entry.Factory);
            }

            return Task.FromResult<ISystemLeaseFactory?>(null);
        }
    }

    /// <summary>
    /// Forces an immediate refresh of the database list.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshFactoriesAsync(cancellationToken).ConfigureAwait(false);
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
            // Dispose all factories if they implement IDisposable
            foreach (var entry in factoriesByIdentifier.Values)
            {
                (entry.Factory as IDisposable)?.Dispose();
            }

            factoriesByIdentifier.Clear();
            currentFactories.Clear();
        }

        refreshSemaphore?.Dispose();
    }

    private async Task RefreshFactoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Discovering lease databases...");
            var configs = await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<LeaseDatabaseConfig>();

            lock (lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>();

                // Update or add factories
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!factoriesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        logger.LogInformation(
                            "Discovered new lease database: {Identifier}",
                            config.Identifier);

                        var factoryLogger = loggerFactory.CreateLogger<SqlLeaseFactory>();
                        var factory = new SqlLeaseFactory(
                            new LeaseFactoryConfig
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                RenewPercent = 0.6,
                                GateTimeoutMs = 200,
                                UseGate = false,
                            },
                            factoryLogger);

                        entry = new FactoryEntry
                        {
                            Identifier = config.Identifier,
                            Factory = factory,
                            Config = config,
                        };

                        factoriesByIdentifier[config.Identifier] = entry;
                        currentFactories.Add(factory);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                    else if (entry.Config.ConnectionString != config.ConnectionString ||
                             entry.Config.SchemaName != config.SchemaName)
                    {
                        // Configuration changed - recreate the factory
                        logger.LogInformation(
                            "Lease database configuration changed for {Identifier}, recreating factory",
                            config.Identifier);

                        currentFactories.Remove(entry.Factory);

                        // Dispose old instance if it implements IDisposable
                        (entry.Factory as IDisposable)?.Dispose();

                        var factoryLogger = loggerFactory.CreateLogger<SqlLeaseFactory>();
                        var factory = new SqlLeaseFactory(
                            new LeaseFactoryConfig
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                RenewPercent = 0.6,
                                GateTimeoutMs = 200,
                                UseGate = false,
                            },
                            factoryLogger);

                        entry.Factory = factory;
                        entry.Config = config;

                        currentFactories.Add(factory);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                }

                // Remove factories that are no longer present
                var removedIdentifiers = factoriesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    logger.LogInformation(
                        "Lease database removed: {Identifier}",
                        identifier);

                    var entry = factoriesByIdentifier[identifier];

                    // Dispose factory if it implements IDisposable
                    (entry.Factory as IDisposable)?.Dispose();

                    currentFactories.Remove(entry.Factory);
                    factoriesByIdentifier.Remove(identifier);
                }

                logger.LogDebug(
                    "Discovery complete. Managing {Count} lease databases",
                    factoriesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    logger.LogInformation(
                        "Deploying lease schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed lease schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy lease schema for database: {Identifier}. Factory will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error discovering lease databases. Continuing with existing configuration.");
        }
    }

    private sealed class FactoryEntry
    {
        public required string Identifier { get; set; }

        public required ISystemLeaseFactory Factory { get; set; }

        public required LeaseDatabaseConfig Config { get; set; }
    }
}
