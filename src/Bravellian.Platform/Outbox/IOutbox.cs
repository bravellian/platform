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

using System.Data;
using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;
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
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message into the outbox table using the configured connection string.
    /// This method creates its own connection and transaction for reliability.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message into the outbox table using the configured connection string.
    /// This method creates its own connection and transaction for reliability.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <param name="dueTimeUtc">An optional timestamp indicating when the message should become eligible for processing.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message into the outbox table within the context
    /// of an existing database transaction.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message into the outbox table within the context
    /// of an existing database transaction.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message into the outbox table within the context
    /// of an existing database transaction.
    /// </summary>
    /// <param name="topic">The topic or type of the message, used for routing.</param>
    /// <param name="payload">The message content, typically serialized as a string (e.g., JSON).</param>
    /// <param name="transaction">The database transaction to participate in.</param>
    /// <param name="correlationId">An optional ID to trace the message back to its source.</param>
    /// <param name="dueTimeUtc">An optional timestamp indicating when the message should become eligible for processing.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims ready outbox messages atomically with a lease for processing.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the claiming process.</param>
    /// <param name="leaseSeconds">The duration in seconds to hold the lease.</param>
    /// <param name="batchSize">The maximum number of items to claim.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of claimed message identifiers.</returns>
    Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges outbox messages as successfully processed.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to acknowledge.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AckAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abandons outbox messages, returning them to the ready state for retry.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to abandon.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AbandonAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks outbox messages as failed with error information.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of messages to fail.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task FailAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reaps expired outbox messages, returning them to ready state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReapExpiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts a new join to track a group of related outbox messages.
    /// </summary>
    /// <param name="tenantId">The PayeWaive tenant identifier.</param>
    /// <param name="expectedSteps">The total number of steps expected to complete.</param>
    /// <param name="metadata">Optional metadata (JSON string) for the join.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The join ID of the newly created join.</returns>
    Task<JoinIdentifier> StartJoinAsync(
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
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AttachMessageToJoinAsync(
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports that a step in a join has completed successfully.
    /// This operation is idempotent when called with the same outboxMessageId.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="outboxMessageId">The outbox message identifier that completed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReportStepCompletedAsync(
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports that a step in a join has failed.
    /// This operation is idempotent when called with the same outboxMessageId.
    /// </summary>
    /// <param name="joinId">The join identifier.</param>
    /// <param name="outboxMessageId">The outbox message identifier that failed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReportStepFailedAsync(
        JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken);
}
