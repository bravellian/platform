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
/// Provides data access operations for the outbox join store,
/// enabling fan-in/join semantics for outbox messages.
/// </summary>
public interface IOutboxJoinStore
{
    /// <summary>
    /// Creates a new join to track a group of related outbox messages.
    /// </summary>
    /// <param name="tenantId">The PayeWaive tenant identifier.</param>
    /// <param name="expectedSteps">The total number of steps expected to complete.</param>
    /// <param name="metadata">Optional metadata (JSON string) for the join.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created join with its assigned JoinId.</returns>
    Task<OutboxJoin> CreateJoinAsync(
        long tenantId,
        int expectedSteps,
        string? metadata,
        CancellationToken cancellationToken);

    /// <summary>
    /// Associates an outbox message with a join.
    /// This operation is idempotent - calling it multiple times with the same parameters has no additional effect.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="outboxMessageId">The outbox message identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AttachMessageToJoinAsync(
        JoinId joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current state of a join.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The join, or null if not found.</returns>
    Task<OutboxJoin?> GetJoinAsync(JoinId joinId, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the completed steps counter for a join.
    /// This operation is idempotent when called with the same outboxMessageId.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="outboxMessageId">The outbox message identifier that completed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated join.</returns>
    Task<OutboxJoin> IncrementCompletedAsync(
        JoinId joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Increments the failed steps counter for a join.
    /// This operation is idempotent when called with the same outboxMessageId.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="outboxMessageId">The outbox message identifier that failed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated join.</returns>
    Task<OutboxJoin> IncrementFailedAsync(
        JoinId joinId,
        Guid outboxMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the status of a join.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateStatusAsync(
        JoinId joinId,
        byte status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all outbox message IDs associated with a join.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of outbox message IDs.</returns>
    Task<IReadOnlyList<Guid>> GetJoinMessagesAsync(
        JoinId joinId,
        CancellationToken cancellationToken);
}
