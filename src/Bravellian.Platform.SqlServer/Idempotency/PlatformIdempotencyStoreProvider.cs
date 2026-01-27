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

using System.Collections.Concurrent;
using Bravellian.Platform.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal sealed class PlatformIdempotencyStoreProvider : IIdempotencyStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformIdempotencyStoreProvider> logger;
    private readonly string tableName;
    private readonly Lock lockObject = new();
    private readonly Dictionary<string, IIdempotencyStore> storesByKey = new(StringComparer.Ordinal);
    private IReadOnlyList<IIdempotencyStore>? cachedStores;
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new(StringComparer.Ordinal);
    private readonly bool enableSchemaDeployment;
    private readonly PlatformConfiguration? platformConfiguration;
    private readonly TimeSpan lockDuration;
    private readonly Func<string, TimeSpan>? lockDurationProvider;

    public PlatformIdempotencyStoreProvider(
        IPlatformDatabaseDiscovery discovery,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string tableName,
        TimeSpan lockDuration,
        Func<string, TimeSpan>? lockDurationProvider = null,
        bool enableSchemaDeployment = true,
        PlatformConfiguration? platformConfiguration = null)
    {
        this.discovery = discovery;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<PlatformIdempotencyStoreProvider>();
        this.tableName = tableName;
        this.lockDuration = lockDuration;
        this.lockDurationProvider = lockDurationProvider;
        this.enableSchemaDeployment = enableSchemaDeployment;
        this.platformConfiguration = platformConfiguration;
    }

    public async Task<IReadOnlyList<IIdempotencyStore>> GetAllStoresAsync()
    {
        if (cachedStores == null)
        {
            logger.LogDebug("Starting platform database discovery for idempotency stores");

            var databases = (await discovery.DiscoverDatabasesAsync().ConfigureAwait(false)).ToList();
            logger.LogDebug(
                "Discovery returned {Count} database(s): {Databases}",
                databases.Count,
                string.Join(", ", databases.Select(FormatDatabase)));

            if (platformConfiguration?.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                !string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneConnectionString))
            {
                foreach (var db in databases)
                {
                    if (IsSameConnection(db.ConnectionString, platformConfiguration.ControlPlaneConnectionString))
                    {
                        logger.LogWarning(
                            "Discovered database {Database} matches the configured control plane connection. " +
                            "Idempotency stores should typically exclude the control plane. Check your discovery source.",
                            FormatDatabase(db));
                    }
                }
            }

            lock (lockObject)
            {
                if (cachedStores == null)
                {
                    var stores = new List<IIdempotencyStore>();
                    var newDatabases = new List<PlatformDatabase>();

                    foreach (var db in databases)
                    {
                        if (platformConfiguration?.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                            !string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString) &&
                            IsSameConnection(db.ConnectionString, platformConfiguration.ControlPlaneConnectionString))
                        {
                            logger.LogDebug(
                                "Skipping idempotency store registration for control plane database: {Database}",
                                FormatDatabase(db));
                            continue;
                        }

                        var options = new SqlIdempotencyOptions
                        {
                            ConnectionString = db.ConnectionString,
                            SchemaName = db.SchemaName,
                            TableName = tableName,
                            LockDuration = lockDuration,
                            LockDurationProvider = lockDurationProvider,
                            EnableSchemaDeployment = enableSchemaDeployment,
                        };

                        var storeLogger = loggerFactory.CreateLogger<SqlIdempotencyStore>();
                        var store = new SqlIdempotencyStore(
                            Options.Create(options),
                            timeProvider,
                            storeLogger);

                        stores.Add(store);
                        storesByKey[db.Name] = store;

                        if (enableSchemaDeployment && schemasDeployed.TryAdd(db.Name, 0))
                        {
                            newDatabases.Add(db);
                        }
                    }

                    cachedStores = stores;

                    if (newDatabases.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (var db in newDatabases)
                            {
                                try
                                {
                                    logger.LogInformation(
                                        "Deploying idempotency schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureIdempotencySchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        tableName).ConfigureAwait(false);

                                    logger.LogInformation(
                                        "Successfully deployed idempotency schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(
                                        ex,
                                        "Failed to deploy idempotency schema for database: {DatabaseName}. Store may fail on first use.",
                                        db.Name);
                                }
                            }
                        });
                    }
                }
            }
        }

        return cachedStores;
    }

    public string GetStoreIdentifier(IIdempotencyStore store)
    {
        foreach (var kvp in storesByKey)
        {
            if (ReferenceEquals(kvp.Value, store))
            {
                return kvp.Key;
            }
        }

        return "unknown";
    }

    public IIdempotencyStore? GetStoreByKey(string key)
    {
        if (cachedStores == null)
        {
            GetAllStoresAsync().GetAwaiter().GetResult();
        }

        return storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    private static string FormatDatabase(PlatformDatabase db)
    {
        return $"{db.Name} (Schema: {db.SchemaName}, Catalog: {TryGetCatalog(db.ConnectionString)})";
    }

    private static string TryGetCatalog(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.InitialCatalog)
                ? "<unknown>"
                : builder.InitialCatalog;
        }
        catch
        {
            return "<unparsed>";
        }
    }

    private static bool IsSameConnection(string a, string b)
    {
        try
        {
            var builderA = new SqlConnectionStringBuilder(a);
            var builderB = new SqlConnectionStringBuilder(b);
            return string.Equals(builderA.DataSource, builderB.DataSource, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(builderA.InitialCatalog, builderB.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
