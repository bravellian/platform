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
/// Provides work-queue style operations for the inbox store.
/// Mirrors the work queue pattern used by Outbox, Timers, and JobRuns.
/// </summary>
public interface IInboxWorkStore
{
    /// <summary>
    /// Claims ready inbox messages for processing with a lease/lock mechanism.
    /// </summary>
    /// <param name="ownerToken">Unique token identifying the claiming worker.</param>
    /// <param name="leaseSeconds">Number of seconds to hold the lease.</param>
    /// <param name="batchSize">Maximum number of messages to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of claimed message IDs.</returns>
    Task<IReadOnlyList<string>> ClaimAsync(Bravellian.Platform.OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges successful processing of messages, marking them as Done.
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to acknowledge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AckAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Abandons processing of messages, returning them to Ready state for retry.
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to abandon.</param>
    /// <param name="lastError">Optional error message to record for troubleshooting.</param>
    /// <param name="delay">Optional delay before the message becomes eligible for retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AbandonAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks messages as permanently failed (Dead).
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to fail.</param>
    /// <param name="errorMessage">Error message to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string errorMessage, CancellationToken cancellationToken);

    /// <summary>
    /// Requeues dead messages by resetting them to Seen.
    /// </summary>
    /// <param name="messageIds">IDs of messages to requeue.</param>
    /// <param name="reason">Optional reason or note for the requeue.</param>
    /// <param name="delay">Optional delay before the message becomes eligible again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReviveAsync(IEnumerable<string> messageIds, string? reason = null, TimeSpan? delay = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims expired leases, returning messages to Ready state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReapExpiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific inbox message for processing.
    /// </summary>
    /// <param name="messageId">The message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inbox message.</returns>
    Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken);
}
