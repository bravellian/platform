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
/// Defines a strategy for selecting which inbox work store to poll next when
/// processing messages across multiple databases/tenants.
/// </summary>
public interface IInboxSelectionStrategy
{
    /// <summary>
    /// Selects the next inbox work store to poll for messages.
    /// </summary>
    /// <param name="stores">All available inbox work stores.</param>
    /// <param name="lastProcessedStore">The store that was processed in the last iteration (may be null).</param>
    /// <param name="lastProcessedCount">The number of messages processed from the last store.</param>
    /// <returns>The next store to poll, or null if no store should be polled.</returns>
    IInboxWorkStore? SelectNext(
        IReadOnlyList<IInboxWorkStore> stores,
        IInboxWorkStore? lastProcessedStore,
        int lastProcessedCount);

    /// <summary>
    /// Resets the strategy state (e.g., when all stores have been processed).
    /// </summary>
    void Reset();
}
