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
    Task<IReadOnlyList<string>> ClaimAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges successful processing of messages, marking them as Done.
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to acknowledge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AckAsync(Guid ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Abandons processing of messages, returning them to Ready state for retry.
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to abandon.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AbandonAsync(Guid ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Marks messages as permanently failed (Dead).
    /// </summary>
    /// <param name="ownerToken">Token of the worker that claimed the messages.</param>
    /// <param name="messageIds">IDs of messages to fail.</param>
    /// <param name="error">Error message to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailAsync(Guid ownerToken, IEnumerable<string> messageIds, string error, CancellationToken cancellationToken);

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
    Task<IInboxMessage> GetAsync(string messageId, CancellationToken cancellationToken);
}