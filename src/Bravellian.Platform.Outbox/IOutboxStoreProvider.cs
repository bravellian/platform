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
/// Provides access to multiple outbox stores, enabling cross-database outbox processing.
/// This abstraction allows the system to poll and process messages from multiple customer
/// databases, each with their own outbox table.
/// </summary>
internal interface IOutboxStoreProvider
{
    /// <summary>
    /// Gets all available outbox stores that should be processed.
    /// Each store represents a separate database/tenant with its own outbox.
    /// </summary>
    /// <returns>A read-only list of outbox stores to poll.</returns>
    Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync();

    /// <summary>
    /// Gets a unique identifier for an outbox store (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="store">The outbox store.</param>
    /// <returns>A human-readable identifier for the store.</returns>
    string GetStoreIdentifier(IOutboxStore store);

    /// <summary>
    /// Gets an outbox store by its identifier key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The outbox store for the specified key, or null if not found.</returns>
    IOutboxStore? GetStoreByKey(string key);

    /// <summary>
    /// Gets an outbox service instance for the specified routing key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The outbox service for the specified key, or null if not found.</returns>
    IOutbox? GetOutboxByKey(string key);
}
