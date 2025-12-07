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

namespace Bravellian.Platform.Tests.TestUtilities;

/// <summary>
/// A mock implementation of IOutboxStore for testing purposes.
/// Returns empty results for all operations.
/// </summary>
public class MockOutboxStore : IOutboxStore
{
    private readonly string name;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockOutboxStore"/> class.
    /// </summary>
    /// <param name="name">The name identifier for this mock store.</param>
    public MockOutboxStore(string name)
    {
        this.name = name;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage>());

    /// <inheritdoc/>
    public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public override string ToString() => name;
}
