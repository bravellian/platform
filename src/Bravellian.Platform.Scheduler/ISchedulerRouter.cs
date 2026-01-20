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
/// Routes scheduler write operations to the appropriate scheduler database based on a routing key.
/// This enables multi-tenant scenarios where scheduler operations need to be written to different
/// database instances based on tenant ID or other routing criteria.
/// </summary>
public interface ISchedulerRouter
{
    /// <summary>
    /// Gets a scheduler client instance for the specified routing key.
    /// </summary>
    /// <param name="routingKey">The key used to determine which scheduler to use (e.g., tenant ID, customer ID).</param>
    /// <returns>A scheduler client instance for the specified routing key.</returns>
    /// <exception cref="ArgumentNullException">When routingKey is null.</exception>
    /// <exception cref="InvalidOperationException">When no scheduler client can be found for the routing key.</exception>
    ISchedulerClient GetSchedulerClient(string routingKey);

    /// <summary>
    /// Gets a scheduler client instance for the specified routing key.
    /// </summary>
    /// <param name="routingKey">The GUID key used to determine which scheduler to use.</param>
    /// <returns>A scheduler client instance for the specified routing key.</returns>
    /// <exception cref="InvalidOperationException">When no scheduler client can be found for the routing key.</exception>
    ISchedulerClient GetSchedulerClient(Guid routingKey);
}
