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
/// Routes outbox write operations to the appropriate outbox database based on a routing key.
/// This enables multi-tenant scenarios where messages need to be written to different
/// database instances based on tenant ID or other routing criteria.
/// </summary>
public interface IOutboxRouter
{
    /// <summary>
    /// Gets an outbox instance for the specified routing key.
    /// </summary>
    /// <param name="routingKey">The key used to determine which outbox to use (e.g., tenant ID, customer ID).</param>
    /// <returns>An outbox instance for the specified routing key.</returns>
    /// <exception cref="ArgumentNullException">When routingKey is null.</exception>
    /// <exception cref="InvalidOperationException">When no outbox can be found for the routing key.</exception>
    IOutbox GetOutbox(string routingKey);

    /// <summary>
    /// Gets an outbox instance for the specified routing key.
    /// </summary>
    /// <param name="routingKey">The GUID key used to determine which outbox to use.</param>
    /// <returns>An outbox instance for the specified routing key.</returns>
    /// <exception cref="InvalidOperationException">When no outbox can be found for the routing key.</exception>
    IOutbox GetOutbox(Guid routingKey);
}
