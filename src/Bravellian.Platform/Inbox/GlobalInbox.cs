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
/// Global inbox implementation that forwards to a routed inbox.
/// </summary>
internal sealed class GlobalInbox : IGlobalInbox
{
    private readonly IInbox inner;

    public GlobalInbox(IInbox inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<bool> AlreadyProcessedAsync(InboxMessageIdentifier messageId, string source, CancellationToken cancellationToken) =>
        inner.AlreadyProcessedAsync(messageId, source, cancellationToken);

    public Task<bool> AlreadyProcessedAsync(InboxMessageIdentifier messageId, string source, byte[]? hash, CancellationToken cancellationToken) =>
        inner.AlreadyProcessedAsync(messageId, source, hash, cancellationToken);

    public Task MarkProcessedAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken) =>
        inner.MarkProcessedAsync(messageId, cancellationToken);

    public Task MarkProcessingAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken) =>
        inner.MarkProcessingAsync(messageId, cancellationToken);

    public Task MarkDeadAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken) =>
        inner.MarkDeadAsync(messageId, cancellationToken);

    public Task EnqueueAsync(string topic, string source, InboxMessageIdentifier messageId, string payload, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, source, messageId, payload, cancellationToken);

    public Task EnqueueAsync(string topic, string source, InboxMessageIdentifier messageId, string payload, byte[]? hash, CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, source, messageId, payload, hash, cancellationToken);

    public Task EnqueueAsync(
        string topic,
        string source,
        InboxMessageIdentifier messageId,
        string payload,
        byte[]? hash,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken) =>
        inner.EnqueueAsync(topic, source, messageId, payload, hash, dueTimeUtc, cancellationToken);
}
