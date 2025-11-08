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
/// Provides a mechanism for discovering fanout database configurations dynamically.
/// Implementations can query a registry, database, or configuration service to get
/// the current list of customer databases.
/// </summary>
public interface IFanoutDatabaseDiscovery
{
    /// <summary>
    /// Discovers all fanout database configurations that should be processed.
    /// This method is called periodically to detect new or removed databases.
    /// </summary>
    /// <returns>Collection of fanout options for all discovered databases.</returns>
    Task<IEnumerable<FanoutDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a single fanout database.
/// </summary>
public sealed class FanoutDatabaseConfig
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
    /// Gets or sets the schema name for the fanout tables. Defaults to "dbo".
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the table name for the fanout policy. Defaults to "FanoutPolicy".
    /// </summary>
    public string PolicyTableName { get; set; } = "FanoutPolicy";

    /// <summary>
    /// Gets or sets the table name for the fanout cursor. Defaults to "FanoutCursor".
    /// </summary>
    public string CursorTableName { get; set; } = "FanoutCursor";

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated when the database is first discovered.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}

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
    private readonly object lockObject = new();
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly Dictionary<string, RepositoryEntry> repositoriesByIdentifier = new();
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
        await this.EnsureRefreshedAsync(cancellationToken).ConfigureAwait(false);

        lock (this.lockObject)
        {
            // Return defensive copy to prevent external mutation
            return this.currentPolicyRepositories.ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        await this.EnsureRefreshedAsync(cancellationToken).ConfigureAwait(false);

        lock (this.lockObject)
        {
            // Return defensive copy to prevent external mutation
            return this.currentCursorRepositories.ToList();
        }
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        lock (this.lockObject)
        {
            foreach (var entry in this.repositoriesByIdentifier.Values)
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
        lock (this.lockObject)
        {
            foreach (var entry in this.repositoriesByIdentifier.Values)
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
        lock (this.lockObject)
        {
            if (this.repositoriesByIdentifier.TryGetValue(key, out var entry))
            {
                return entry.PolicyRepository;
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public IFanoutCursorRepository? GetCursorRepositoryByKey(string key)
    {
        lock (this.lockObject)
        {
            if (this.repositoriesByIdentifier.TryGetValue(key, out var entry))
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
        await this.RefreshRepositoriesAsync(cancellationToken).ConfigureAwait(false);
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
            // Dispose all repositories if they implement IDisposable
            foreach (var entry in this.repositoriesByIdentifier.Values)
            {
                (entry.PolicyRepository as IDisposable)?.Dispose();
                (entry.CursorRepository as IDisposable)?.Dispose();
            }

            this.repositoriesByIdentifier.Clear();
            this.currentPolicyRepositories.Clear();
            this.currentCursorRepositories.Clear();
        }

        this.refreshSemaphore?.Dispose();
    }

    private async Task EnsureRefreshedAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        bool needsRefresh;
        lock (this.lockObject)
        {
            needsRefresh = (now - this.lastRefresh >= this.refreshInterval);
        }

        if (needsRefresh)
        {
            // Try to acquire the semaphore immediately
            if (await this.refreshSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // Double-check in case another thread already refreshed
                    now = this.timeProvider.GetUtcNow();
                    lock (this.lockObject)
                    {
                        needsRefresh = (now - this.lastRefresh >= this.refreshInterval);
                    }

                    if (needsRefresh)
                    {
                        await this.RefreshRepositoriesAsync(cancellationToken).ConfigureAwait(false);
                        lock (this.lockObject)
                        {
                            this.lastRefresh = now;
                        }
                    }
                }
                finally
                {
                    this.refreshSemaphore.Release();
                }
            }
            else
            {
                // Wait for the ongoing refresh to complete
                await this.refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                this.refreshSemaphore.Release();
            }
        }
    }

    private async Task RefreshRepositoriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogDebug("Discovering fanout databases...");
            var configs = await this.discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
            var configList = configs.ToList();

            // Track configurations that need schema deployment
            var schemasToDeploy = new List<FanoutDatabaseConfig>();

            lock (this.lockObject)
            {
                // Track which identifiers we've seen in this refresh
                var seenIdentifiers = new HashSet<string>();

                // Update or add repositories
                foreach (var config in configList)
                {
                    seenIdentifiers.Add(config.Identifier);

                    if (!this.repositoriesByIdentifier.TryGetValue(config.Identifier, out var entry))
                    {
                        // New database discovered
                        this.logger.LogInformation(
                            "Discovered new fanout database: {Identifier}",
                            config.Identifier);

                        var policyRepo = this.CreatePolicyRepository(config);
                        var cursorRepo = this.CreateCursorRepository(config);

                        entry = new RepositoryEntry
                        {
                            Identifier = config.Identifier,
                            PolicyRepository = policyRepo,
                            CursorRepository = cursorRepo,
                            Config = config,
                        };

                        this.repositoriesByIdentifier[config.Identifier] = entry;
                        this.currentPolicyRepositories.Add(policyRepo);
                        this.currentCursorRepositories.Add(cursorRepo);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                    else if (entry.Config.ConnectionString != config.ConnectionString ||
                             entry.Config.SchemaName != config.SchemaName ||
                             entry.Config.PolicyTableName != config.PolicyTableName ||
                             entry.Config.CursorTableName != config.CursorTableName)
                    {
                        // Configuration changed - recreate the repositories
                        this.logger.LogInformation(
                            "Fanout database configuration changed for {Identifier}, recreating repositories",
                            config.Identifier);

                        this.currentPolicyRepositories.Remove(entry.PolicyRepository);
                        this.currentCursorRepositories.Remove(entry.CursorRepository);

                        // Dispose old instances if they implement IDisposable
                        (entry.PolicyRepository as IDisposable)?.Dispose();
                        (entry.CursorRepository as IDisposable)?.Dispose();

                        var policyRepo = this.CreatePolicyRepository(config);
                        var cursorRepo = this.CreateCursorRepository(config);

                        entry.PolicyRepository = policyRepo;
                        entry.CursorRepository = cursorRepo;
                        entry.Config = config;

                        this.currentPolicyRepositories.Add(policyRepo);
                        this.currentCursorRepositories.Add(cursorRepo);

                        // Mark for schema deployment
                        if (config.EnableSchemaDeployment)
                        {
                            schemasToDeploy.Add(config);
                        }
                    }
                }

                // Remove repositories that are no longer present
                var removedIdentifiers = this.repositoriesByIdentifier.Keys
                    .Where(id => !seenIdentifiers.Contains(id))
                    .ToList();

                foreach (var identifier in removedIdentifiers)
                {
                    this.logger.LogInformation(
                        "Fanout database removed: {Identifier}",
                        identifier);

                    var entry = this.repositoriesByIdentifier[identifier];

                    // Dispose repositories if they implement IDisposable
                    (entry.PolicyRepository as IDisposable)?.Dispose();
                    (entry.CursorRepository as IDisposable)?.Dispose();

                    this.currentPolicyRepositories.Remove(entry.PolicyRepository);
                    this.currentCursorRepositories.Remove(entry.CursorRepository);
                    this.repositoriesByIdentifier.Remove(identifier);
                }

                this.logger.LogDebug(
                    "Discovery complete. Managing {Count} fanout databases",
                    this.repositoriesByIdentifier.Count);
            }

            // Deploy schemas outside the lock for databases that need it
            foreach (var config in schemasToDeploy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    this.logger.LogInformation(
                        "Deploying fanout schema for database: {Identifier}",
                        config.Identifier);

                    await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
                        config.ConnectionString,
                        config.SchemaName,
                        config.PolicyTableName,
                        config.CursorTableName).ConfigureAwait(false);

                    this.logger.LogInformation(
                        "Successfully deployed fanout schema for database: {Identifier}",
                        config.Identifier);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(
                        ex,
                        "Failed to deploy fanout schema for database: {Identifier}. Repository will be available but may fail on first use.",
                        config.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(
                ex,
                "Error discovering fanout databases. Continuing with existing configuration.");
        }
    }

    private SqlFanoutOptions CreateSqlFanoutOptions(FanoutDatabaseConfig config)
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
        return new SqlFanoutPolicyRepository(Options.Create(this.CreateSqlFanoutOptions(config)));
    }

    private SqlFanoutCursorRepository CreateCursorRepository(FanoutDatabaseConfig config)
    {
        return new SqlFanoutCursorRepository(Options.Create(this.CreateSqlFanoutOptions(config)));
    }

    private sealed class RepositoryEntry
    {
        public required string Identifier { get; set; }

        public required IFanoutPolicyRepository PolicyRepository { get; set; }

        public required IFanoutCursorRepository CursorRepository { get; set; }

        public required FanoutDatabaseConfig Config { get; set; }
    }
}
