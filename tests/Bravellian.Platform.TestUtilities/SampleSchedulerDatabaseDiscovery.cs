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
/// Sample implementation of ISchedulerDatabaseDiscovery for testing purposes.
/// Allows adding and removing databases dynamically to simulate discovery scenarios.
/// </summary>
public class SampleSchedulerDatabaseDiscovery : ISchedulerDatabaseDiscovery
{
    private readonly List<SchedulerDatabaseConfig> databases = new();

    public SampleSchedulerDatabaseDiscovery(IEnumerable<SchedulerDatabaseConfig>? initialDatabases = null)
    {
        if (initialDatabases != null)
        {
            databases.AddRange(initialDatabases);
        }
    }

    public Task<IEnumerable<SchedulerDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
    {
        // Return a copy to prevent external modification
        return Task.FromResult<IEnumerable<SchedulerDatabaseConfig>>(databases.ToList());
    }

    public void AddDatabase(SchedulerDatabaseConfig config)
    {
        databases.Add(config);
    }

    public void RemoveDatabase(string identifier)
    {
        databases.RemoveAll(db => string.Equals(db.Identifier, identifier, StringComparison.Ordinal));
    }

    public void ClearDatabases()
    {
        databases.Clear();
    }
}

