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
/// Round-robin selection strategy that cycles through all outbox stores,
/// processing one batch from each store before moving to the next.
/// This ensures fair distribution of processing across all databases.
/// This class is thread-safe.
/// </summary>
public sealed class RoundRobinOutboxSelectionStrategy : IOutboxSelectionStrategy
{
    private int currentIndex;

    /// <inheritdoc/>
    public IOutboxStore? SelectNext(
        IReadOnlyList<IOutboxStore> stores,
        IOutboxStore? lastProcessedStore,
        int lastProcessedCount)
    {
        ArgumentNullException.ThrowIfNull(stores);

        if (stores.Count == 0)
        {
            return null;
        }

        // If we have a last processed store, find it and move to the next
        if (lastProcessedStore != null)
        {
            var lastIndex = FindStoreIndex(stores, lastProcessedStore);
            if (lastIndex >= 0)
            {
                // Use Interlocked to ensure thread-safe update
                System.Threading.Interlocked.Exchange(ref currentIndex, (lastIndex + 1) % stores.Count);
            }
        }

        // Atomically get the current index and increment it
        var index = currentIndex;
        var selected = stores[index];
        System.Threading.Interlocked.CompareExchange(ref currentIndex, (index + 1) % stores.Count, index);
        return selected;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        System.Threading.Interlocked.Exchange(ref currentIndex, 0);
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
