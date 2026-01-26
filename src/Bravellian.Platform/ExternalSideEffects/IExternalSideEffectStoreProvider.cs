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
/// Provides external side-effect stores.
/// </summary>
public interface IExternalSideEffectStoreProvider
{
    /// <summary>
    /// Gets all configured stores.
    /// </summary>
    /// <returns>The list of stores.</returns>
    Task<IReadOnlyList<IExternalSideEffectStore>> GetAllStoresAsync();

    /// <summary>
    /// Gets a human readable identifier for a store.
    /// </summary>
    /// <param name="store">The store instance.</param>
    /// <returns>The store identifier.</returns>
    string GetStoreIdentifier(IExternalSideEffectStore store);

    /// <summary>
    /// Gets a store by its key.
    /// </summary>
    /// <param name="key">The store key.</param>
    /// <returns>The store, if found.</returns>
    IExternalSideEffectStore? GetStoreByKey(string key);
}
