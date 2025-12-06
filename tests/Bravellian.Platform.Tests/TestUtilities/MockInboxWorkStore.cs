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

namespace Bravellian.Platform.Tests.TestUtilities;

/// <summary>
/// A mock implementation of IInboxWorkStore for testing purposes.
/// Returns empty results for all operations.
/// </summary>
public class MockInboxWorkStore : IInboxWorkStore
{
    private readonly string name;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockInboxWorkStore"/> class.
    /// </summary>
    /// <param name="name">The name identifier for this mock store.</param>
    public MockInboxWorkStore(string name)
    {
        this.name = name;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ClaimAsync(Bravellian.Platform.OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>(new List<string>());

    /// <inheritdoc/>
    public Task AckAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task AbandonAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task FailAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string error, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReapExpiredAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new InboxMessage
        {
            MessageId = messageId,
            Source = "TestSource",
            Topic = "Test.Topic",
            Payload = "Test payload",
            Hash = null,
            Attempt = 0,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow,
        });
    }

    /// <inheritdoc/>
    public override string ToString() => name;
}
