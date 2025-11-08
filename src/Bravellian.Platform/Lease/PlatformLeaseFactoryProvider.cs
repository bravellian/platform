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
/// Lease factory provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformLeaseFactoryProvider : ILeaseFactoryProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly ILoggerFactory loggerFactory;
    private readonly object lockObject = new();
    private IReadOnlyList<ISystemLeaseFactory>? cachedFactories;
    private readonly Dictionary<string, ISystemLeaseFactory> factoriesByKey = new();

    public PlatformLeaseFactoryProvider(
        IPlatformDatabaseDiscovery discovery,
        ILoggerFactory loggerFactory)
    {
        this.discovery = discovery;
        this.loggerFactory = loggerFactory;
    }

    public IReadOnlyList<ISystemLeaseFactory> GetAllFactories()
    {
        if (this.cachedFactories == null)
        {
            lock (this.lockObject)
            {
                if (this.cachedFactories == null)
                {
                    var databases = this.discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult();
                    var factories = new List<ISystemLeaseFactory>();
            
                    foreach (var db in databases)
            {
                var factoryLogger = this.loggerFactory.CreateLogger<SqlLeaseFactory>();
                var factory = new SqlLeaseFactory(
                    Options.Create(new SystemLeaseOptions
                    {
                        ConnectionString = db.ConnectionString,
                        SchemaName = db.SchemaName,
                    }),
                    factoryLogger);
                
                    factories.Add(factory);
                    this.factoriesByKey[db.Name] = factory;
                }
            
                this.cachedFactories = factories;
                }
            }
        }
        
        return this.cachedFactories;
    }

    public string GetFactoryIdentifier(ISystemLeaseFactory factory)
    {
        // Find the database name for this factory
        foreach (var kvp in this.factoriesByKey)
        {
            if (ReferenceEquals(kvp.Value, factory))
            {
                return kvp.Key;
            }
        }
        
        return "unknown";
    }

    public ISystemLeaseFactory GetFactoryByKey(string key)
    {
        if (this.cachedFactories == null)
        {
            GetAllFactories(); // Initialize factories
        }
        
        return this.factoriesByKey.TryGetValue(key, out var factory)
            ? factory
            : throw new KeyNotFoundException($"No lease factory found for key: {key}");
    }
}
