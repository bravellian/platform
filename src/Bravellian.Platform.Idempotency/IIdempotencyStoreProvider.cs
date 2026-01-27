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

namespace Bravellian.Platform.Idempotency;

/// <summary>
/// Provides access to idempotency stores by key.
/// </summary>
public interface IIdempotencyStoreProvider
{
    /// <summary>
    /// Gets all configured stores.
    /// </summary>
    /// <returns>All idempotency stores.</returns>
    Task<IReadOnlyList<IIdempotencyStore>> GetAllStoresAsync();

    /// <summary>
    /// Gets a stable identifier for a store.
    /// </summary>
    /// <param name="store">Store instance.</param>
    /// <returns>Store identifier.</returns>
    string GetStoreIdentifier(IIdempotencyStore store);

    /// <summary>
    /// Gets the store for the provided key.
    /// </summary>
    /// <param name="key">Store key.</param>
    /// <returns>The store, or null if not found.</returns>
    IIdempotencyStore? GetStoreByKey(string key);
}
