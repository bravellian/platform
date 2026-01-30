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

internal sealed class InMemoryInboxWorkStore : IInboxWorkStore
{
    private readonly InMemoryInboxState state;

    public InMemoryInboxWorkStore(InMemoryInboxState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Task<IReadOnlyList<string>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
    {
        var claimed = state.Claim(ownerToken, leaseSeconds, batchSize);
        return Task.FromResult<IReadOnlyList<string>>(claimed);
    }

    public Task AckAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken)
    {
        state.Ack(ownerToken, messageIds);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        state.Abandon(ownerToken, messageIds, lastError, delay);
        return Task.CompletedTask;
    }

    public Task FailAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string errorMessage, CancellationToken cancellationToken)
    {
        state.Fail(ownerToken, messageIds, errorMessage);
        return Task.CompletedTask;
    }

    public Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        state.ReapExpired();
        return Task.CompletedTask;
    }

    public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
    {
        return Task.FromResult(state.Get(messageId));
    }
}
