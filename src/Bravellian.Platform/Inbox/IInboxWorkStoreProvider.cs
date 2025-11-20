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
/// Provides access to multiple inbox work stores, enabling cross-database inbox processing.
/// This abstraction allows the system to poll and process messages from multiple customer
/// databases, each with their own inbox table.
/// </summary>
internal interface IInboxWorkStoreProvider
{
    /// <summary>
    /// Gets all available inbox work stores that should be processed.
    /// Each store represents a separate database/tenant with its own inbox.
    /// </summary>
    /// <returns>A read-only list of inbox work stores to poll.</returns>
    Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync();

    /// <summary>
    /// Gets a unique identifier for an inbox work store (e.g., database name, tenant ID).
    /// This is used for logging and diagnostics.
    /// </summary>
    /// <param name="store">The inbox work store.</param>
    /// <returns>A human-readable identifier for the store.</returns>
    string GetStoreIdentifier(IInboxWorkStore store);

    /// <summary>
    /// Gets an inbox work store by its identifier key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The inbox work store for the specified key, or null if not found.</returns>
    IInboxWorkStore? GetStoreByKey(string key);

    /// <summary>
    /// Gets an inbox service instance for the specified routing key.
    /// This is used for routing write operations to the correct database.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The inbox service for the specified key, or null if not found.</returns>
    IInbox? GetInboxByKey(string key);
}
