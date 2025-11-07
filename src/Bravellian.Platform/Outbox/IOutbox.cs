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
using System.Data;
using System.Threading.Tasks;

/// <summary>
/// Provides a mechanism to enqueue messages for later processing
/// as part of a transactional operation, and to claim and process
/// messages using a reliable work queue pattern.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueues a message into the outbox table using the configured connection string.
    /// This method creates its own connection and transaction for reliability.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId);

    /// <summary>
    /// Enqueues a message into the outbox table within the context
    /// of an existing database transaction.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null);

    /// <summary>
    /// Enqueues a message into a specific outbox, identified by a key.
    /// This method creates its own connection and transaction.
    /// </summary>
    /// <param name="key">The key identifying the target outbox store.</param>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        object key,
        string topic,
        string payload,
        string? correlationId);

    /// <summary>
    /// Enqueues a message into a specific outbox, identified by a key,
    /// within the context of an existing database transaction.
    /// </summary>
    /// <param name="key">The key identifying the target outbox store.</param>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        object key,
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null);

    /// <summary>
    /// Claims ready outbox messages atomically with a lease for processing.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the claiming process.</param>
    /// <param name="leaseSeconds">The duration in seconds to hold the lease.</param>
    /// <param name="batchSize">The maximum number of items to claim.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of claimed message identifiers.</returns>
    Task<IReadOnlyList<Guid>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges outbox messages as successfully processed.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to acknowledge.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AckAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons outbox messages, returning them to the ready state for retry.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to abandon.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks outbox messages as failed with error information.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to fail.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task FailAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reaps expired outbox messages, returning them to ready state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReapExpiredAsync(CancellationToken cancellationToken = default);
}
