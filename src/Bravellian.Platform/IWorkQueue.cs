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
/// Generic interface for work queue operations using claim-and-process pattern.
/// </summary>
/// <typeparam name="T">The type of items in the queue.</typeparam>
public interface IWorkQueue<T>
{
    /// <summary>
    /// Claims a batch of ready items from the queue atomically.
    /// </summary>
    /// <param name="ownerToken">Unique identifier for the claiming worker.</param>
    /// <param name="leaseSeconds">Duration in seconds to hold the lease.</param>
    /// <param name="batchSize">Maximum number of items to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of item IDs that were successfully claimed.</returns>
    Task<IReadOnlyList<T>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges successful processing of items, marking them as completed.
    /// </summary>
    /// <param name="ownerToken">The owner token that claimed these items.</param>
    /// <param name="ids">Collection of item IDs to acknowledge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task AckAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons claimed items, returning them to ready state for other workers.
    /// </summary>
    /// <param name="ownerToken">The owner token that claimed these items.</param>
    /// <param name="ids">Collection of item IDs to abandon.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks items as failed with optional error information.
    /// </summary>
    /// <param name="ownerToken">The owner token that claimed these items.</param>
    /// <param name="ids">Collection of item IDs to mark as failed.</param>
    /// <param name="errorMessage">Optional error message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task FailAsync(
        Guid ownerToken,
        IEnumerable<T> ids,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns expired leases to ready state (reaps orphaned items).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of items that were reaped.</returns>
    Task<int> ReapExpiredAsync(CancellationToken cancellationToken = default);
}