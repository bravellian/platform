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
/// Drain-first selection strategy that continues to poll the same inbox work store
/// until it returns no messages, then moves to the next store.
/// This is useful for prioritizing complete processing of one database before
/// moving to others.
/// This class is thread-safe.
/// </summary>
public sealed class DrainFirstInboxSelectionStrategy : IInboxSelectionStrategy
{
    private int currentIndex = 0;
    private readonly object lockObject = new();

    /// <inheritdoc/>
    public IInboxWorkStore? SelectNext(
        IReadOnlyList<IInboxWorkStore> stores,
        IInboxWorkStore? lastProcessedStore,
        int lastProcessedCount)
    {
        lock (this.lockObject)
        {
            if (stores.Count == 0)
            {
                return null;
            }

            // If the last store still had messages, keep processing it
            if (lastProcessedStore != null && lastProcessedCount > 0)
            {
                var lastIndex = FindStoreIndex(stores, lastProcessedStore);
                if (lastIndex >= 0)
                {
                    this.currentIndex = lastIndex;
                    return stores[this.currentIndex];
                }
            }

            // Last store was empty (or null), move to next store
            if (lastProcessedStore != null)
            {
                var lastIndex = FindStoreIndex(stores, lastProcessedStore);
                if (lastIndex >= 0)
                {
                    this.currentIndex = (lastIndex + 1) % stores.Count;
                }
            }

            // Bounds check in case stores changed
            if (this.currentIndex >= stores.Count)
            {
                this.currentIndex = 0;
            }

            return stores[this.currentIndex];
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (this.lockObject)
        {
            this.currentIndex = 0;
        }
    }

    private static int FindStoreIndex(IReadOnlyList<IInboxWorkStore> stores, IInboxWorkStore store)
    {
        for (int i = 0; i < stores.Count; i++)
        {
            if (ReferenceEquals(stores[i], store))
            {
                return i;
            }
        }

        return -1;
    }
}
