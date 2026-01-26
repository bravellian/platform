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
/// Round-robin selection strategy that cycles through all inbox work stores,
/// processing one batch from each store before moving to the next.
/// This ensures fair distribution of processing across all databases.
/// This class is thread-safe.
/// </summary>
public sealed class RoundRobinInboxSelectionStrategy : IInboxSelectionStrategy
{
    private int currentIndex;
    private readonly Lock lockObject = new();

    /// <inheritdoc/>
    public IInboxWorkStore? SelectNext(
        IReadOnlyList<IInboxWorkStore> stores,
        IInboxWorkStore? lastProcessedStore,
        int lastProcessedCount)
    {
        ArgumentNullException.ThrowIfNull(stores);

        lock (lockObject)
        {
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
                    currentIndex = (lastIndex + 1) % stores.Count;
                }
            }

            // Bounds check in case stores changed
            if (currentIndex >= stores.Count)
            {
                currentIndex = 0;
            }

            var selected = stores[currentIndex];
            currentIndex = (currentIndex + 1) % stores.Count;
            return selected;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (lockObject)
        {
            currentIndex = 0;
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
