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
/// Default implementation of IInboxRouter that uses an IInboxWorkStoreProvider
/// to route write operations to the appropriate inbox database.
/// </summary>
internal sealed class InboxRouter : IInboxRouter
{
    private readonly IInboxWorkStoreProvider storeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxRouter"/> class.
    /// </summary>
    /// <param name="storeProvider">The store provider to use for routing.</param>
    public InboxRouter(IInboxWorkStoreProvider storeProvider)
    {
        this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
    }

    /// <inheritdoc/>
    public IInbox GetInbox(string routingKey)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
        {
            throw new ArgumentException("Routing key cannot be null, empty, or whitespace.", nameof(routingKey));
        }

        var inbox = this.storeProvider.GetInboxByKey(routingKey);
        if (inbox == null)
        {
            throw new InvalidOperationException($"No inbox found for routing key: {routingKey}");
        }

        return inbox;
    }

    /// <inheritdoc/>
    public IInbox GetInbox(Guid routingKey)
    {
        return this.GetInbox(routingKey.ToString());
    }
}
