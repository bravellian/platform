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
/// Provides a mechanism to track processed inbound messages for at-most-once processing guarantees.
/// Implements the Inbox pattern to prevent duplicate message processing.
/// </summary>
public interface IInbox
{
    /// <summary>
    /// Checks if a message has already been processed, or records it as seen if it's new.
    /// This method implements MERGE/UPSERT semantics to handle concurrent access safely.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="source">The source system or component that sent the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the message was already processed, false if this is the first time seeing it.</returns>
    Task<bool> AlreadyProcessedAsync(
        string messageId,
        string source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a message has already been processed, or records it as seen if it's new.
    /// This method implements MERGE/UPSERT semantics to handle concurrent access safely.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="source">The source system or component that sent the message.</param>
    /// <param name="hash">Optional content hash for additional verification.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the message was already processed, false if this is the first time seeing it.</returns>
    Task<bool> AlreadyProcessedAsync(
        string messageId,
        string source,
        byte[]? hash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a message as successfully processed.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkProcessedAsync(
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a message as being processed to support poison message detection.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkProcessingAsync(
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a message as dead/poison after repeated failures.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkDeadAsync(
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message for processing by the inbox dispatcher.
    /// </summary>
    /// <param name="topic">The topic to route the message to an appropriate handler.</param>
    /// <param name="source">The source system or component that sent the message.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="payload">The message payload content.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string source,
        string messageId,
        string payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message for processing by the inbox dispatcher.
    /// </summary>
    /// <param name="topic">The topic to route the message to an appropriate handler.</param>
    /// <param name="source">The source system or component that sent the message.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="payload">The message payload content.</param>
    /// <param name="hash">Optional content hash for deduplication.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string source,
        string messageId,
        string payload,
        byte[]? hash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a message for processing by the inbox dispatcher.
    /// </summary>
    /// <param name="topic">The topic to route the message to an appropriate handler.</param>
    /// <param name="source">The source system or component that sent the message.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="payload">The message payload content.</param>
    /// <param name="hash">Optional content hash for deduplication.</param>
    /// <param name="dueTimeUtc">Optional due time for delayed processing. Message will not be processed before this time.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnqueueAsync(
        string topic,
        string source,
        string messageId,
        string payload,
        byte[]? hash,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken);
}
