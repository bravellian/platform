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
/// Platform-level database discovery interface used by all features (Outbox, Inbox, Timers, Jobs, Fan-out).
/// Responsible for returning the set of application databases to work with.
/// Implementations must be read-only, idempotent, and must not perform schema changes or connect to control plane.
/// </summary>
public interface IPlatformDatabaseDiscovery
{
    /// <summary>
    /// Discovers and returns the current set of application databases.
    /// This method is called periodically during platform operation.
    /// Implementations should support caching as appropriate for the polling cycle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of platform databases. Must not return null or empty.</returns>
    Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default);
}
