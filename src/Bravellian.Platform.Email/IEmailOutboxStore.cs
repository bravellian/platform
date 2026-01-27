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

namespace Bravellian.Platform.Email;

/// <summary>
/// Defines storage for queued outbound email messages.
/// </summary>
public interface IEmailOutboxStore
{
    /// <summary>
    /// Determines whether the outbox already contains a matching message key.
    /// </summary>
    /// <param name="messageKey">The stable message key.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the outbox already has the key.</returns>
    Task<bool> AlreadyEnqueuedAsync(
        string messageKey,
        string providerName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a new outbound email.
    /// </summary>
    /// <param name="item">Outbox item to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueAsync(EmailOutboxItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Dequeues the next batch of pending outbox items.
    /// </summary>
    /// <param name="maxItems">Maximum number of items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pending outbox items.</returns>
    Task<IReadOnlyList<EmailOutboxItem>> DequeueAsync(int maxItems, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an outbox item as successfully dispatched.
    /// </summary>
    /// <param name="outboxId">Outbox identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkSucceededAsync(Guid outboxId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an outbox item as failed.
    /// </summary>
    /// <param name="outboxId">Outbox identifier.</param>
    /// <param name="failureReason">Optional failure reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkFailedAsync(Guid outboxId, string? failureReason, CancellationToken cancellationToken);
}
