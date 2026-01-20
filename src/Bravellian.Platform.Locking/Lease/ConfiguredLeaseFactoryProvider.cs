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
/// Provides access to a pre-configured list of lease factories.
/// Each lease factory represents a separate database/tenant.
/// </summary>
internal sealed class ConfiguredLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly Dictionary<string, FactoryEntry> factoriesByIdentifier = new(StringComparer.Ordinal);
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
        logger = loggerFactory.CreateLogger<ConfiguredLeaseFactoryProvider>();

        foreach (var config in this.configs)
        {
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

            var entry = new FactoryEntry
            {
                Identifier = config.Identifier,
                Factory = factory,
            };

            factoriesByIdentifier[config.Identifier] = entry;
            allFactories.Add(factory);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ISystemLeaseFactory>>(allFactories);

    /// <inheritdoc/>
    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
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

    /// <inheritdoc/>
    public Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (factoriesByIdentifier.TryGetValue(key, out var entry))
        {
            return Task.FromResult<ISystemLeaseFactory?>(entry.Factory);
        }

        return Task.FromResult<ISystemLeaseFactory?>(null);
    }

    private sealed class FactoryEntry
    {
        public required string Identifier { get; set; }

        public required ISystemLeaseFactory Factory { get; set; }
    }
}
