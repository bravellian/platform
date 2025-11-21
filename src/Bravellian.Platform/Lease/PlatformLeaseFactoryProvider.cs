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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Lease factory provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<PlatformLeaseFactoryProvider> logger;
    private readonly PlatformConfiguration? platformConfiguration;
    private readonly bool enableSchemaDeployment;
    private readonly object lockObject = new();
    private readonly Dictionary<string, ISystemLeaseFactory> factoriesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<ISystemLeaseFactory, string> identifiersByFactory = new();
    private readonly ConcurrentDictionary<string, byte> schemasDeployed = new(StringComparer.Ordinal);
    private IReadOnlyList<ISystemLeaseFactory>? cachedFactories;

    public PlatformLeaseFactoryProvider(
        IPlatformDatabaseDiscovery discovery,
        ILoggerFactory loggerFactory,
        PlatformConfiguration? platformConfiguration = null,
        bool enableSchemaDeployment = false)
    {
        this.discovery = discovery;
        this.loggerFactory = loggerFactory;
        this.platformConfiguration = platformConfiguration;
        this.enableSchemaDeployment = enableSchemaDeployment;
        logger = loggerFactory.CreateLogger<PlatformLeaseFactoryProvider>();
    }

    public async Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default)
    {
        if (cachedFactories == null)
        {
            var databases = (await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false)).ToList();

            logger.LogDebug(
                "Discovered {Count} platform databases for lease factories: {Names}",
                databases.Count,
                string.Join(", ", databases.Select(d => $"{d.Name} (Schema: {d.SchemaName})")));

            if (platformConfiguration?.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl &&
                !string.IsNullOrWhiteSpace(platformConfiguration.ControlPlaneConnectionString))
            {
                foreach (var db in databases)
                {
                    if (IsSameConnection(db.ConnectionString, platformConfiguration.ControlPlaneConnectionString))
                    {
                        logger.LogWarning(
                            "Database {DatabaseName} matches the configured control plane connection. Leases should run in tenant databases. Verify discovery excludes the control plane.",
                            db.Name);
                    }
                }
            }

            lock (lockObject)
            {
                if (cachedFactories == null)
                {
                    var factories = new List<ISystemLeaseFactory>();
                    var newDatabasesNeedingSchema = new List<PlatformDatabase>();

                    foreach (var db in databases)
                    {
                        var factoryLogger = loggerFactory.CreateLogger<SqlLeaseFactory>();
                        var factory = new SqlLeaseFactory(
                            new LeaseFactoryConfig
                            {
                                ConnectionString = db.ConnectionString,
                                SchemaName = db.SchemaName,
                                RenewPercent = 0.6,
                                GateTimeoutMs = 200,
                                UseGate = false,
                            },
                            factoryLogger);

                        factories.Add(factory);
                        factoriesByKey[db.Name] = factory;
                        identifiersByFactory[factory] = db.Name;

                        if (enableSchemaDeployment && schemasDeployed.TryAdd(db.Name, 0))
                        {
                            newDatabasesNeedingSchema.Add(db);
                        }
                    }

                    cachedFactories = factories;

                    if (newDatabasesNeedingSchema.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (var db in newDatabasesNeedingSchema)
                            {
                                try
                                {
                                    logger.LogInformation(
                                        "Deploying lease schema for newly discovered database: {DatabaseName}",
                                        db.Name);

                                    await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
                                        db.ConnectionString,
                                        db.SchemaName,
                                        "Lease").ConfigureAwait(false);

                                    logger.LogInformation(
                                        "Successfully deployed lease schema for database: {DatabaseName}",
                                        db.Name);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(
                                        ex,
                                        "Failed to deploy lease schema for database: {DatabaseName}. Leases may fail on first use.",
                                        db.Name);
                                }
                            }
                        });
                    }
                }
            }
        }

        return cachedFactories;
    }

    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        if (identifiersByFactory.TryGetValue(factory, out var id))
        {
            return id;
        }

        return "unknown";
    }

    public async Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (cachedFactories == null)
        {
            await GetAllFactoriesAsync(cancellationToken).ConfigureAwait(false);
        }

        return factoriesByKey.TryGetValue(key, out var factory)
            ? factory
            : null;
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
