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
/// Represents a distributed system lease with fencing token support.
/// </summary>
public interface ISystemLease : IAsyncDisposable
{
    /// <summary>
    /// Gets the name of the resource this lease is for.
    /// </summary>
    string ResourceName { get; }

    /// <summary>
    /// Gets the unique token identifying the owner of this lease.
    /// </summary>
    Guid OwnerToken { get; }

    /// <summary>
    /// Gets the current fencing token for this lease.
    /// This token increments on each acquire and renew operation.
    /// </summary>
    long FencingToken { get; }

    /// <summary>
    /// Gets a cancellation token that is canceled when the lease is lost.
    /// This allows work to be cooperatively canceled when the lease expires or is lost.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Throws <see cref="LostLeaseException"/> if the lease has been lost.
    /// </summary>
    /// <exception cref="LostLeaseException">Thrown when the lease has been lost.</exception>
    void ThrowIfLost();

    /// <summary>
    /// Attempts to renew the lease immediately.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>True if the lease was successfully renewed, false if it was lost.</returns>
    Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default);
}
