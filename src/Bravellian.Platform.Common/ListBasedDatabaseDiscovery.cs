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

/// <summary>
/// List-based implementation of platform database discovery.
/// Returns a static, pre-configured list of databases.
/// </summary>
internal sealed class ListBasedDatabaseDiscovery : IPlatformDatabaseDiscovery
{
    private readonly IReadOnlyCollection<PlatformDatabase> databases;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListBasedDatabaseDiscovery"/> class.
    /// </summary>
    /// <param name="databases">The list of databases to return.</param>
    public ListBasedDatabaseDiscovery(IReadOnlyCollection<PlatformDatabase> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        if (databases.Count == 0)
        {
            throw new ArgumentException("Database list must not be empty.", nameof(databases));
        }

        // Validate unique names
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var db in databases)
        {
            if (!uniqueNames.Add(db.Name))
            {
                throw new ArgumentException($"Duplicate database name found: '{db.Name}'. All database names must be unique.", nameof(databases));
            }
        }

        this.databases = databases;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(databases);
    }
}
