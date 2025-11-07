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
/// Sample implementation of IInboxDatabaseDiscovery for testing and demonstration purposes.
/// This implementation returns a static list of databases but can be used as a template
/// for real implementations that query a database or registry.
/// </summary>
public class SampleInboxDatabaseDiscovery : IInboxDatabaseDiscovery
{
    private readonly List<InboxDatabaseConfig> databases = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleInboxDatabaseDiscovery"/> class
    /// with an optional initial set of databases.
    /// </summary>
    /// <param name="initialDatabases">Optional initial set of databases.</param>
    public SampleInboxDatabaseDiscovery(IEnumerable<InboxDatabaseConfig>? initialDatabases = null)
    {
        if (initialDatabases != null)
        {
            this.databases.AddRange(initialDatabases);
        }
    }

    /// <summary>
    /// Adds a database to the discovery list. Used for testing dynamic behavior.
    /// </summary>
    public void AddDatabase(InboxDatabaseConfig config)
    {
        this.databases.Add(config);
    }

    /// <summary>
    /// Removes a database from the discovery list. Used for testing dynamic behavior.
    /// </summary>
    public void RemoveDatabase(string identifier)
    {
        this.databases.RemoveAll(db => db.Identifier == identifier);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<InboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would query a database or registry
        // For example:
        // - Query a global database: SELECT CustomerId, ConnectionString FROM Customers WHERE IsActive = 1
        // - Query a configuration API
        // - Read from a configuration file or service
        return Task.FromResult<IEnumerable<InboxDatabaseConfig>>(this.databases.ToList());
    }
}
