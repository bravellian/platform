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
        return this.GetAllFactoriesAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
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
                    await this.RefreshFactoriesAsync(cancellationToken).ConfigureAwait(false);
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
            return this.currentFactories.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        lock (this.lockObject)
        {
            foreach (var entry in this.factoriesByIdentifier.Values)
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
    public ISystemLeaseFactory? GetFactoryByKey(string key)
    {
        lock (this.lockObject)
        {
            if (this.factoriesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.Factory;
            }

            return null;
        }
    }

    /// <summary>
    /// Forces an immediate refresh of the database list.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await this.RefreshFactoriesAsync(cancellationToken).ConfigureAwait(false);
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
            // Dispose all factories if they implement IDisposable
            foreach (var entry in this.factoriesByIdentifier.Values)
            {
                (entry.Factory as IDisposable)?.Dispose();
            }

            this.factoriesByIdentifier.Clear();
            this.currentFactories.Clear();
        }

        this.refreshSemaphore?.Dispose();
    }

    private async Task RefreshFactoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogDebug("Discovering lease databases...");
            var configs = await this.discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<LeaseDatabaseConfig>();

            lock (this.lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>();

                // Update or add factories
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!this.factoriesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        this.logger.LogInformation(
                            "Discovered new lease database: {Identifier}",
                            config.Identifier);

                        var factoryLogger = this.loggerFactory.CreateLogger<SqlLeaseFactory>();
                        var factory = new SqlLeaseFactory(
                            Options.Create(new SystemLeaseOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            factoryLogger);

                        entry = new FactoryEntry
                        {
                            Identifier = config.Identifier,
                            Factory = factory,
                            Config = config,
                        };

                        this.factoriesByIdentifier[config.Identifier] = entry;
                        this.currentFactories.Add(factory);

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
                        this.logger.LogInformation(
                            "Lease database configuration changed for {Identifier}, recreating factory",
                            config.Identifier);

                        this.currentFactories.Remove(entry.Factory);

                        // Dispose old instance if it implements IDisposable
                        (entry.Factory as IDisposable)?.Dispose();

                        var factoryLogger = this.loggerFactory.CreateLogger<SqlLeaseFactory>();
                        var factory = new SqlLeaseFactory(
                            Options.Create(new SystemLeaseOptions
                            {
                                ConnectionString = config.ConnectionString,
                                SchemaName = config.SchemaName,
                                EnableSchemaDeployment = config.EnableSchemaDeployment,
                            }),
                            factoryLogger);

                        entry.Factory = factory;
                        entry.Config = config;

                        this.currentFactories.Add(factory);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                }

                // Remove factories that are no longer present
                var removedIdentifiers = this.factoriesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    this.logger.LogInformation(
                        "Lease database removed: {Identifier}",
                        identifier);

                    var entry = this.factoriesByIdentifier[identifier];
                    
                    // Dispose factory if it implements IDisposable
                    (entry.Factory as IDisposable)?.Dispose();

                    this.currentFactories.Remove(entry.Factory);
                    this.factoriesByIdentifier.Remove(identifier);
                }

                this.logger.LogDebug(
                    "Discovery complete. Managing {Count} lease databases",
                    this.factoriesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    this.logger.LogInformation(
                        "Deploying lease schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName).ConfigureAwait(false);

                    this.logger.LogInformation(
                        "Successfully deployed lease schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(
                        ex,
                        "Failed to deploy lease schema for database: {Identifier}. Factory will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(
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
