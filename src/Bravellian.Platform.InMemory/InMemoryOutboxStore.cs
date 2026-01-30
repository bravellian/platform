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

using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;

internal sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly InMemoryOutboxState state;
    private readonly InMemoryOutboxJoinStore? joinStore;
    private readonly OwnerToken ownerToken;
    private readonly int leaseSeconds;
    private readonly TimeProvider timeProvider;

    public InMemoryOutboxStore(
        InMemoryOutboxState state,
        InMemoryOutboxJoinStore? joinStore,
        InMemoryOutboxOptions options,
        TimeProvider timeProvider)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.joinStore = joinStore;
        ownerToken = OwnerToken.GenerateNew();
        leaseSeconds = (int)options.LeaseDuration.TotalSeconds;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        var claimed = state.ClaimDue(ownerToken, leaseSeconds, limit);
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
    }

    public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
    {
        state.Ack(ownerToken, new[] { id.Value });
        var message = state.GetMessage(id.Value);
        if (message != null)
        {
            joinStore?.MarkMessageCompleted(message.MessageId);
        }

        return Task.CompletedTask;
    }

    public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dueTime = delay > TimeSpan.Zero ? now.Add(delay) : (DateTimeOffset?)now;
        state.Abandon(ownerToken, new[] { id.Value }, lastError, dueTime);
        return Task.CompletedTask;
    }

    public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
    {
        state.Fail(ownerToken, new[] { id.Value }, lastError, $"{Environment.MachineName}:FAILED");
        var message = state.GetMessage(id.Value);
        if (message != null)
        {
            joinStore?.MarkMessageFailed(message.MessageId);
        }

        return Task.CompletedTask;
    }
}
