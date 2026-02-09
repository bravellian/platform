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

internal sealed class InMemoryInboxService : IInbox
{
    private readonly InMemoryInboxState state;

    public InMemoryInboxService(InMemoryInboxState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<bool> AlreadyProcessedAsync(InboxMessageIdentifier messageId, string source, CancellationToken cancellationToken)
    {
        return AlreadyProcessedAsync(messageId, source, null, cancellationToken);
    }

    public Task<bool> AlreadyProcessedAsync(InboxMessageIdentifier messageId, string source, byte[]? hash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        var result = state.AlreadyProcessed(messageId.Value, source, hash);
        return Task.FromResult(result);
    }

    public Task MarkProcessedAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        state.MarkProcessed(messageId.Value);
        return Task.CompletedTask;
    }

    public Task MarkProcessingAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        state.MarkProcessing(messageId.Value);
        return Task.CompletedTask;
    }

    public Task MarkDeadAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        state.MarkDead(messageId.Value);
        return Task.CompletedTask;
    }

    public Task EnqueueAsync(string topic, string source, InboxMessageIdentifier messageId, string payload, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, source, messageId, payload, null, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string source, InboxMessageIdentifier messageId, string payload, byte[]? hash, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, source, messageId, payload, hash, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string source, InboxMessageIdentifier messageId, string payload, byte[]? hash, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be null or empty", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source cannot be null or empty", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(messageId.Value))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload cannot be null or empty", nameof(payload));
        }

        state.Enqueue(topic, source, messageId.Value, payload, hash, dueTimeUtc);
        return Task.CompletedTask;
    }
}
