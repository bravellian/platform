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

namespace Bravellian.Platform.Tests.TestUtilities;

/// <summary>
/// Sample implementation of IOutboxDatabaseDiscovery for testing and demonstration purposes.
/// This implementation returns a static list of databases but can be used as a template
/// for real implementations that query a database or registry.
/// </summary>
public class SampleOutboxDatabaseDiscovery : IOutboxDatabaseDiscovery
{
    private readonly List<OutboxDatabaseConfig> databases = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleOutboxDatabaseDiscovery"/> class
    /// with an optional initial set of databases.
    /// </summary>
    /// <param name="initialDatabases">Optional initial set of databases.</param>
    public SampleOutboxDatabaseDiscovery(IEnumerable<OutboxDatabaseConfig>? initialDatabases = null)
    {
        if (initialDatabases != null)
        {
            databases.AddRange(initialDatabases);
        }
    }

    /// <summary>
    /// Adds a database to the discovery list. Used for testing dynamic behavior.
    /// </summary>
    public void AddDatabase(OutboxDatabaseConfig config)
    {
        databases.Add(config);
    }

    /// <summary>
    /// Removes a database from the discovery list. Used for testing dynamic behavior.
    /// </summary>
    public void RemoveDatabase(string identifier)
    {
        databases.RemoveAll(db => string.Equals(db.Identifier, identifier, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would query a database or registry
        // For example:
        // - Query a global database: SELECT CustomerId, ConnectionString FROM Customers WHERE IsActive = 1
        // - Query a configuration API
        // - Read from a configuration file or service
        return Task.FromResult<IEnumerable<OutboxDatabaseConfig>>(databases.ToList());
    }
}
