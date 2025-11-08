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

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// Internal service for distributed semaphore operations backed by the control plane.
/// </summary>
public interface ISemaphoreService
{
    /// <summary>
    /// Attempts to acquire a semaphore lease.
    /// </summary>
    /// <param name="name">The semaphore name.</param>
    /// <param name="ttlSeconds">The TTL for the lease in seconds.</param>
    /// <param name="ownerId">The stable owner identifier.</param>
    /// <param name="clientRequestId">Optional idempotency key for retries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the lease was acquired.</returns>
    Task<SemaphoreAcquireResult> TryAcquireAsync(
        string name,
        int ttlSeconds,
        string ownerId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing semaphore lease.
    /// </summary>
    /// <param name="name">The semaphore name.</param>
    /// <param name="token">The lease token.</param>
    /// <param name="ttlSeconds">The TTL for the renewed lease in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the lease was renewed.</returns>
    Task<SemaphoreRenewResult> RenewAsync(
        string name,
        Guid token,
        int ttlSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a semaphore lease.
    /// </summary>
    /// <param name="name">The semaphore name.</param>
    /// <param name="token">The lease token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the lease was released.</returns>
    Task<SemaphoreReleaseResult> ReleaseAsync(
        string name,
        Guid token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reaps expired leases for a semaphore.
    /// </summary>
    /// <param name="name">The semaphore name (null for all semaphores).</param>
    /// <param name="maxRows">Maximum number of rows to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of expired leases deleted.</returns>
    Task<int> ReapExpiredAsync(
        string? name = null,
        int maxRows = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a semaphore exists with the specified limit.
    /// </summary>
    /// <param name="name">The semaphore name.</param>
    /// <param name="limit">The concurrency limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureExistsAsync(
        string name,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the limit for an existing semaphore.
    /// </summary>
    /// <param name="name">The semaphore name.</param>
    /// <param name="newLimit">The new concurrency limit.</param>
    /// <param name="ensureIfMissing">Whether to create the semaphore if it doesn't exist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateLimitAsync(
        string name,
        int newLimit,
        bool ensureIfMissing = false,
        CancellationToken cancellationToken = default);
}
