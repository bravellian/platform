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
/// Default implementation of IOutboxRouter that uses an IOutboxStoreProvider
/// to route write operations to the appropriate outbox database.
/// </summary>
internal sealed class OutboxRouter : IOutboxRouter
{
    private readonly IOutboxStoreProvider storeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxRouter"/> class.
    /// </summary>
    /// <param name="storeProvider">The store provider to use for routing.</param>
    public OutboxRouter(IOutboxStoreProvider storeProvider)
    {
        this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
    }

    /// <inheritdoc/>
    public IOutbox GetOutbox(string routingKey)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
        {
            throw new ArgumentException("Routing key cannot be null, empty, or whitespace.", nameof(routingKey));
        }

        var outbox = storeProvider.GetOutboxByKey(routingKey);
        if (outbox == null)
        {
            throw new InvalidOperationException($"No outbox found for routing key: {routingKey}");
        }

        return outbox;
    }

    /// <inheritdoc/>
    public IOutbox GetOutbox(Guid routingKey)
    {
        return GetOutbox(routingKey.ToString());
    }
}
