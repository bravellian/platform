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
/// Provides access to multiple fanout repositories that are discovered dynamically at runtime.
/// This implementation queries an IFanoutDatabaseDiscovery service to detect new or
/// removed databases and manages the lifecycle of fanout repositories accordingly.
/// </summary>
internal sealed class DynamicFanoutRepositoryProvider : IFanoutRepositoryProvider, IDisposable
{
    private readonly IFanoutDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DynamicFanoutRepositoryProvider> logger;
    private readonly Lock lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, RepositoryEntry> repositoriesByIdentifier = new(StringComparer.Ordinal);
    private readonly List<IFanoutPolicyRepository> currentPolicyRepositories = new();
    private readonly List<IFanoutCursorRepository> currentCursorRepositories = new();
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;
    private readonly TimeSpan refreshInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicFanoutRepositoryProvider"/> class.
    /// </summary>
    /// <param name="discovery">The database discovery service.</param>
    /// <param name="timeProvider">Time provider for refresh interval tracking.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for this provider.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    public DynamicFanoutRepositoryProvider(
        IFanoutDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<DynamicFanoutRepositoryProvider> logger,
        TimeSpan? refreshInterval = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IFanoutPolicyRepository>> GetAllPolicyRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRefreshedAsync(cancellationToken).ConfigureAwait(false);

        lock (lockObject)
        {
            // Return defensive copy to prevent external mutation
            return currentPolicyRepositories.ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRefreshedAsync(cancellationToken).ConfigureAwait(false);

        lock (lockObject)
        {
            // Return defensive copy to prevent external mutation
            return currentCursorRepositories.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        lock (lockObject)
        {
            foreach (var entry in repositoriesByIdentifier.Values)
            {
                if (ReferenceEquals(entry.PolicyRepository, repository))
                {
                    return entry.Identifier;
                }
            }

            return "Unknown";
        }
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutCursorRepository repository)
    {
        lock (lockObject)
        {
            foreach (var entry in repositoriesByIdentifier.Values)
            {
                if (ReferenceEquals(entry.CursorRepository, repository))
                {
                    return entry.Identifier;
                }
            }

            return "Unknown";
        }
    }

    /// <inheritdoc/>
    public IFanoutPolicyRepository? GetPolicyRepositoryByKey(string key)
    {
        lock (lockObject)
        {
            if (repositoriesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.PolicyRepository;
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public IFanoutCursorRepository? GetCursorRepositoryByKey(string key)
    {
        lock (lockObject)
        {
            if (repositoriesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.CursorRepository;
            }

            return null;
        }
    }

    /// <summary>
    /// Forces an immediate refresh of the database list.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshRepositoriesAsync(cancellationToken).ConfigureAwait(false);
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
            // Dispose all repositories if they implement IDisposable
            foreach (var entry in repositoriesByIdentifier.Values)
            {
                (entry.PolicyRepository as IDisposable)?.Dispose();
                (entry.CursorRepository as IDisposable)?.Dispose();
            }

            repositoriesByIdentifier.Clear();
            currentPolicyRepositories.Clear();
            currentCursorRepositories.Clear();
        }

        refreshSemaphore?.Dispose();
    }

    private async Task EnsureRefreshedAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        bool needsRefresh;
        lock (lockObject)
        {
            needsRefresh = (now - lastRefresh >= refreshInterval);
        }

        if (needsRefresh)
        {
            // Try to acquire the semaphore immediately
            if (await refreshSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // Double-check in case another thread already refreshed
                    now = timeProvider.GetUtcNow();
                    lock (lockObject)
                    {
                        needsRefresh = (now - lastRefresh >= refreshInterval);
                    }

                    if (needsRefresh)
                    {
                        await RefreshRepositoriesAsync(cancellationToken).ConfigureAwait(false);
                        lock (lockObject)
                        {
                            lastRefresh = now;
                        }
                    }
                }
                finally
                {
                    refreshSemaphore.Release();
                }
            }
            else
            {
                // Wait for the ongoing refresh to complete
                await refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                refreshSemaphore.Release();
            }
        }
    }

    private async Task RefreshRepositoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Discovering fanout databases...");
            var configs = await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<FanoutDatabaseConfig>();

            lock (lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>(StringComparer.Ordinal);

                // Update or add repositories
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!repositoriesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        logger.LogInformation(
                            "Discovered new fanout database: {Identifier}",
                            config.Identifier);

                        var policyRepo = CreatePolicyRepository(config);
                        var cursorRepo = CreateCursorRepository(config);

                        entry = new RepositoryEntry
                        {
                            Identifier = config.Identifier,
                            PolicyRepository = policyRepo,
                            CursorRepository = cursorRepo,
                            Config = config,
                        };

                        repositoriesByIdentifier[config.Identifier] = entry;
                        currentPolicyRepositories.Add(policyRepo);
                        currentCursorRepositories.Add(cursorRepo);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                    else if (!string.Equals(entry.Config.ConnectionString, config.ConnectionString, StringComparison.Ordinal) ||
!string.Equals(entry.Config.SchemaName, config.SchemaName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.PolicyTableName, config.PolicyTableName, StringComparison.Ordinal) ||
!string.Equals(entry.Config.CursorTableName, config.CursorTableName, StringComparison.Ordinal))
                    {
                        // Configuration changed - recreate the repositories
                        logger.LogInformation(
                            "Fanout database configuration changed for {Identifier}, recreating repositories",
                            config.Identifier);

                        currentPolicyRepositories.Remove(entry.PolicyRepository);
                        currentCursorRepositories.Remove(entry.CursorRepository);

                        // Dispose old instances if they implement IDisposable
                        (entry.PolicyRepository as IDisposable)?.Dispose();
                        (entry.CursorRepository as IDisposable)?.Dispose();

                        var policyRepo = CreatePolicyRepository(config);
                        var cursorRepo = CreateCursorRepository(config);

                        entry.PolicyRepository = policyRepo;
                        entry.CursorRepository = cursorRepo;
                        entry.Config = config;

                        currentPolicyRepositories.Add(policyRepo);
                        currentCursorRepositories.Add(cursorRepo);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                }

                // Remove repositories that are no longer present
                var removedIdentifiers = repositoriesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    logger.LogInformation(
                        "Fanout database removed: {Identifier}",
                        identifier);

                    var entry = repositoriesByIdentifier[identifier];

                    // Dispose repositories if they implement IDisposable
                    (entry.PolicyRepository as IDisposable)?.Dispose();
                    (entry.CursorRepository as IDisposable)?.Dispose();

                    currentPolicyRepositories.Remove(entry.PolicyRepository);
                    currentCursorRepositories.Remove(entry.CursorRepository);
                    repositoriesByIdentifier.Remove(identifier);
                }

                logger.LogDebug(
                    "Discovery complete. Managing {Count} fanout databases",
                    repositoriesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    logger.LogInformation(
                        "Deploying fanout schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName,
                        config.PolicyTableName,
                        config.CursorTableName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed fanout schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy fanout schema for database: {Identifier}. Repository will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error discovering fanout databases. Continuing with existing configuration.");
        }
    }

    private static SqlFanoutOptions CreateSqlFanoutOptions(FanoutDatabaseConfig config)
    {
        return new SqlFanoutOptions
        {
            ConnectionString = config.ConnectionString,
            SchemaName = config.SchemaName,
            PolicyTableName = config.PolicyTableName,
            CursorTableName = config.CursorTableName,
            EnableSchemaDeployment = config.EnableSchemaDeployment,
        };
    }

    private SqlFanoutPolicyRepository CreatePolicyRepository(FanoutDatabaseConfig config)
    {
        return new SqlFanoutPolicyRepository(Options.Create(CreateSqlFanoutOptions(config)));
    }

    private SqlFanoutCursorRepository CreateCursorRepository(FanoutDatabaseConfig config)
    {
        return new SqlFanoutCursorRepository(Options.Create(CreateSqlFanoutOptions(config)));
    }

    private sealed class RepositoryEntry
    {
        public required string Identifier { get; set; }

        public required IFanoutPolicyRepository PolicyRepository { get; set; }

        public required IFanoutCursorRepository CursorRepository { get; set; }

        public required FanoutDatabaseConfig Config { get; set; }
    }
}
