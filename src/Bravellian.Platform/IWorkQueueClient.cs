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
/// Defines the contract for a generic work queue client that supports claim-and-process semantics.
/// </summary>
/// <typeparam name="TId">The type of the queue item identifier (e.g., long, Guid).</typeparam>
public interface IWorkQueueClient<TId>
{
    /// <summary>
    /// Claims ready work items atomically with a lease.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the claiming process.</param>
    /// <param name="leaseSeconds">The duration in seconds to hold the lease.</param>
    /// <param name="batchSize">The maximum number of items to claim.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of claimed item identifiers.</returns>
    Task<IReadOnlyList<TId>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges items as successfully processed.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of items to acknowledge.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AckAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons items, returning them to the ready state for retry.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of items to abandon.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks items as failed with error information.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of items to fail.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task FailAsync(
        Guid ownerToken,
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reaps expired items, returning them to ready state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReapExpiredAsync(CancellationToken cancellationToken = default);
}