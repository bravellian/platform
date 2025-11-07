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
/// Drain-first selection strategy that continues to poll the same outbox store
/// until it returns no messages, then moves to the next store.
/// This is useful for prioritizing complete processing of one database before
/// moving to others.
/// This class is thread-safe.
/// </summary>
public sealed class DrainFirstOutboxSelectionStrategy : IOutboxSelectionStrategy
{
    private int currentIndex = 0;

    /// <inheritdoc/>
    public IOutboxStore? SelectNext(
        IReadOnlyList<IOutboxStore> stores,
        IOutboxStore? lastProcessedStore,
        int lastProcessedCount)
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
                // Use Interlocked to ensure thread-safe update
                System.Threading.Interlocked.Exchange(ref this.currentIndex, lastIndex);
                return stores[this.currentIndex];
            }
        }

        // Last store was empty (or null), move to next store
        if (lastProcessedStore != null)
        {
            var lastIndex = FindStoreIndex(stores, lastProcessedStore);
            if (lastIndex >= 0)
            {
                System.Threading.Interlocked.Exchange(ref this.currentIndex, (lastIndex + 1) % stores.Count);
            }
        }

        return stores[System.Threading.Interlocked.CompareExchange(ref this.currentIndex, this.currentIndex, this.currentIndex)];
    }

    /// <inheritdoc/>
    public void Reset()
    {
        System.Threading.Interlocked.Exchange(ref this.currentIndex, 0);
    }

    private static int FindStoreIndex(IReadOnlyList<IOutboxStore> stores, IOutboxStore store)
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
