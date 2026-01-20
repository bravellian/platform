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
/// Routes lease requests to the correct tenant-specific lease factory.
/// </summary>
public interface ILeaseRouter
{
    /// <summary>
    /// Gets the lease factory for a specific routing key/tenant.
    /// </summary>
    /// <param name="routingKey">The routing key (e.g., tenant identifier).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lease factory for the given key.</returns>
    Task<ISystemLeaseFactory> GetLeaseFactoryAsync(string routingKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default lease factory when only one tenant is configured.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The default lease factory.</returns>
    Task<ISystemLeaseFactory> GetDefaultLeaseFactoryAsync(CancellationToken cancellationToken = default);
}
