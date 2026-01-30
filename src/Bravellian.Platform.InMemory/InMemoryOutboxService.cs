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
using System.Linq;
using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;

internal sealed class InMemoryOutboxService : IOutbox
{
    private readonly InMemoryOutboxState state;
    private readonly InMemoryOutboxJoinStore? joinStore;

    public InMemoryOutboxService(InMemoryOutboxState state, InMemoryOutboxJoinStore? joinStore)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.joinStore = joinStore;
    }

    public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, (string?)null, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        state.Enqueue(topic, payload, correlationId, dueTimeUtc);
        return Task.CompletedTask;
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, transaction, null, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, transaction, correlationId, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return EnqueueAsync(topic, payload, correlationId, dueTimeUtc, cancellationToken);
    }

    public Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
    {
        var claimed = state.Claim(ownerToken, leaseSeconds, batchSize);
        return Task.FromResult<IReadOnlyList<OutboxWorkItemIdentifier>>(claimed);
    }

    public Task AckAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Select(id => id.Value).ToList();
        state.Ack(ownerToken, idList);
        if (joinStore != null)
        {
            foreach (var id in idList)
            {
                var message = state.GetMessage(id);
                if (message != null)
                {
                    joinStore.MarkMessageCompleted(message.MessageId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Select(id => id.Value).ToList();
        state.Abandon(ownerToken, idList, null, null);
        return Task.CompletedTask;
    }

    public Task FailAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Select(id => id.Value).ToList();
        state.Fail(ownerToken, idList, null, $"{Environment.MachineName}:FAILED");
        if (joinStore != null)
        {
            foreach (var id in idList)
            {
                var message = state.GetMessage(id);
                if (message != null)
                {
                    joinStore.MarkMessageFailed(message.MessageId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        state.ReapExpired();
        return Task.CompletedTask;
    }

    public async Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException("Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        var join = await joinStore.CreateJoinAsync(tenantId, expectedSteps, metadata, cancellationToken).ConfigureAwait(false);
        return join.JoinId;
    }

    public Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException("Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        return joinStore.AttachMessageToJoinAsync(joinId, outboxMessageId, cancellationToken);
    }

    public Task ReportStepCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException("Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        return joinStore.IncrementCompletedAsync(joinId, outboxMessageId, cancellationToken);
    }

    public Task ReportStepFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException("Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        return joinStore.IncrementFailedAsync(joinId, outboxMessageId, cancellationToken);
    }
}
