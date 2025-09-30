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

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Factory for creating system leases for distributed coordination.
/// </summary>
public interface ISystemLeaseFactory
{
    /// <summary>
    /// Attempts to acquire a lease for the specified resource.
    /// </summary>
    /// <param name="resourceName">The unique name of the resource to lease.</param>
    /// <param name="leaseDuration">How long the lease should be held.</param>
    /// <param name="contextJson">Optional context information to store with the lease.</param>
    /// <param name="ownerToken">Optional owner token; if not provided, a new one will be generated.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A lease if acquired successfully, null if the resource is already leased.</returns>
    Task<ISystemLease?> AcquireAsync(
        string resourceName,
        TimeSpan leaseDuration,
        string? contextJson = null,
        Guid? ownerToken = null,
        CancellationToken cancellationToken = default);
}