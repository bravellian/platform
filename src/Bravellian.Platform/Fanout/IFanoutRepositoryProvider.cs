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
/// Provides access to multiple fanout repositories, enabling cross-database fanout processing.
/// This abstraction allows the system to manage fanout policies and cursors across multiple customer
/// databases, each with their own fanout tables.
/// </summary>
internal interface IFanoutRepositoryProvider
{
    /// <summary>
    /// Asynchronously gets all available fanout policy repositories that should be processed.
    /// Each repository represents a separate database/tenant with its own fanout tables.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of fanout policy repositories.</returns>
    Task<IReadOnlyList<IFanoutPolicyRepository>> GetAllPolicyRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously gets all available fanout cursor repositories that should be processed.
    /// Each repository represents a separate database/tenant with its own fanout tables.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of fanout cursor repositories.</returns>
    Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a unique identifier for a fanout policy repository (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="repository">The fanout policy repository.</param>
    /// <returns>A human-readable identifier for the repository.</returns>
    string GetRepositoryIdentifier(IFanoutPolicyRepository repository);

    /// <summary>
    /// Gets a unique identifier for a fanout cursor repository (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="repository">The fanout cursor repository.</param>
    /// <returns>A human-readable identifier for the repository.</returns>
    string GetRepositoryIdentifier(IFanoutCursorRepository repository);

    /// <summary>
    /// Gets a fanout policy repository by its identifier key.
    /// This is used for routing operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The fanout policy repository for the specified key, or null if not found.</returns>
    IFanoutPolicyRepository? GetPolicyRepositoryByKey(string key);

    /// <summary>
    /// Gets a fanout cursor repository by its identifier key.
    /// This is used for routing operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The fanout cursor repository for the specified key, or null if not found.</returns>
    IFanoutCursorRepository? GetCursorRepositoryByKey(string key);
}
