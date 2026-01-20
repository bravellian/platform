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
/// Provides access to multiple lease factories, enabling lease management across multiple databases.
/// This abstraction allows the system to acquire and manage leases in multiple customer
/// databases, each with their own lease table.
/// </summary>
public interface ILeaseFactoryProvider
{
    /// <summary>
    /// Gets all available lease factories that should be managed.
    /// Each factory represents a separate database/tenant with its own lease table.
    /// </summary>
    /// <returns>A read-only list of lease factories to manage.</returns>
    Task<IReadOnlyList<ISystemLeaseFactory>> GetAllFactoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a unique identifier for a lease factory (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="factory">The lease factory.</param>
    /// <returns>A human-readable identifier for the factory.</returns>
    string GetFactoryIdentifier(ISystemLeaseFactory factory);

    /// <summary>
    /// Gets a lease factory by its identifier key.
    /// This is used for routing lease operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The lease factory for the specified key, or null if not found.</returns>
    Task<ISystemLeaseFactory?> GetFactoryByKeyAsync(string key, CancellationToken cancellationToken = default);
}
