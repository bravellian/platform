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
/// Default implementation of ISchedulerRouter that uses an ISchedulerStoreProvider
/// to route write operations to the appropriate scheduler database.
/// </summary>
public sealed class SchedulerRouter : ISchedulerRouter
{
    private readonly ISchedulerStoreProvider storeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchedulerRouter"/> class.
    /// </summary>
    /// <param name="storeProvider">The store provider to use for routing.</param>
    public SchedulerRouter(ISchedulerStoreProvider storeProvider)
    {
        this.storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
    }

    /// <inheritdoc/>
    public ISchedulerClient GetSchedulerClient(string routingKey)
    {
        if (string.IsNullOrWhiteSpace(routingKey))
        {
            throw new ArgumentException("Routing key cannot be null, empty, or whitespace.", nameof(routingKey));
        }

        var client = this.storeProvider.GetSchedulerClientByKey(routingKey);
        if (client == null)
        {
            throw new InvalidOperationException($"No scheduler client found for routing key: {routingKey}");
        }

        return client;
    }

    /// <inheritdoc/>
    public ISchedulerClient GetSchedulerClient(Guid routingKey)
    {
        return this.GetSchedulerClient(routingKey.ToString());
    }
}
