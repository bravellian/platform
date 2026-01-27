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
/// Represents a queued email outbox item.
/// </summary>
public sealed record EmailOutboxItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutboxItem"/> class.
    /// </summary>
    /// <param name="id">Outbox identifier.</param>
    /// <param name="providerName">Provider name.</param>
    /// <param name="messageKey">Stable idempotency key.</param>
    /// <param name="message">Outbound email message.</param>
    /// <param name="enqueuedAtUtc">Enqueue timestamp.</param>
    /// <param name="dueTimeUtc">Optional due time.</param>
    /// <param name="attemptCount">Dispatch attempt count.</param>
    public EmailOutboxItem(
        Guid id,
        string providerName,
        string messageKey,
        OutboundEmailMessage message,
        DateTimeOffset enqueuedAtUtc,
        DateTimeOffset? dueTimeUtc,
        int attemptCount)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        if (string.IsNullOrWhiteSpace(messageKey))
        {
            throw new ArgumentException("Message key is required.", nameof(messageKey));
        }

        Id = id;
        ProviderName = providerName;
        MessageKey = messageKey;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        EnqueuedAtUtc = enqueuedAtUtc;
        DueTimeUtc = dueTimeUtc;
        AttemptCount = attemptCount;
    }

    /// <summary>
    /// Gets the outbox identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the stable idempotency key.
    /// </summary>
    public string MessageKey { get; }

    /// <summary>
    /// Gets the outbound email message.
    /// </summary>
    public OutboundEmailMessage Message { get; }

    /// <summary>
    /// Gets the enqueue timestamp.
    /// </summary>
    public DateTimeOffset EnqueuedAtUtc { get; }

    /// <summary>
    /// Gets the optional due time.
    /// </summary>
    public DateTimeOffset? DueTimeUtc { get; }

    /// <summary>
    /// Gets the dispatch attempt count.
    /// </summary>
    public int AttemptCount { get; }
}
