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
/// Provides access to a pre-configured list of lease factories.
/// Each lease factory represents a separate database/tenant.
/// </summary>
internal sealed class ConfiguredLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly Dictionary<string, FactoryEntry> factoriesByIdentifier = new();
    private readonly List<ISystemLeaseFactory> allFactories = new();
    private readonly IReadOnlyList<LeaseDatabaseConfig> configs;
    private readonly ILogger<ConfiguredLeaseFactoryProvider> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredLeaseFactoryProvider"/> class.
    /// </summary>
    /// <param name="configs">List of lease database configurations.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public ConfiguredLeaseFactoryProvider(
        IEnumerable<LeaseDatabaseConfig> configs,
        ILoggerFactory loggerFactory)
    {
        this.configs = configs.ToList();
        this.logger = loggerFactory.CreateLogger<ConfiguredLeaseFactoryProvider>();

        foreach (var config in this.configs)
        {
            var factoryLogger = loggerFactory.CreateLogger<SqlLeaseFactory>();
            var factory = new SqlLeaseFactory(
                Options.Create(new SystemLeaseOptions
                {
                    ConnectionString = config.ConnectionString,
                    SchemaName = config.SchemaName,
                    EnableSchemaDeployment = config.EnableSchemaDeployment,
                }),
                factoryLogger);

            var entry = new FactoryEntry
            {
                Identifier = config.Identifier,
                Factory = factory,
            };

            this.factoriesByIdentifier[config.Identifier] = entry;
            this.allFactories.Add(factory);
        }
    }

    /// <summary>
    /// Initializes the lease factories by deploying database schemas if enabled.
    /// This method should be called after construction to ensure all databases are ready.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var config in this.configs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config.EnableSchemaDeployment)
            {
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
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISystemLeaseFactory> GetAllFactories() => this.allFactories;

    /// <inheritdoc/>
    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
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

    /// <inheritdoc/>
    public ISystemLeaseFactory? GetFactoryByKey(string key)
    {
        if (this.factoriesByIdentifier.TryGetValue(key, out var entry))
        {
            return entry.Factory;
        }

        return null;
    }

    private sealed class FactoryEntry
    {
        public required string Identifier { get; set; }

        public required ISystemLeaseFactory Factory { get; set; }
    }
}
