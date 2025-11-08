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
/// Provides access to multiple scheduler stores, enabling cross-database scheduler processing.
/// This abstraction allows the system to poll and process scheduler work from multiple customer
/// databases, each with their own scheduler tables.
/// </summary>
public interface ISchedulerStoreProvider
{
    /// <summary>
    /// Gets all available scheduler stores that should be processed.
    /// Each store represents a separate database/tenant with its own scheduler tables.
    /// </summary>
    /// <returns>A read-only list of scheduler stores to poll.</returns>
    IReadOnlyList<ISchedulerStore> GetAllStores();

    /// <summary>
    /// Gets a unique identifier for a scheduler store (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="store">The scheduler store.</param>
    /// <returns>A human-readable identifier for the store.</returns>
    string GetStoreIdentifier(ISchedulerStore store);

    /// <summary>
    /// Gets a scheduler store by its identifier key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The scheduler store for the specified key, or null if not found.</returns>
    ISchedulerStore? GetStoreByKey(string key);

    /// <summary>
    /// Gets a scheduler client instance for the specified routing key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The scheduler client for the specified key, or null if not found.</returns>
    ISchedulerClient? GetSchedulerClientByKey(string key);

    /// <summary>
    /// Gets an outbox instance for the specified routing key.
    /// This is used for dispatching scheduler work to the correct outbox.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The outbox for the specified key, or null if not found.</returns>
    IOutbox? GetOutboxByKey(string key);
}
