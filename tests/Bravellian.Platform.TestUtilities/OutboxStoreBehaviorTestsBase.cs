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
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests.TestUtilities;

public abstract class OutboxStoreBehaviorTestsBase : IAsyncLifetime
{
    private readonly IOutboxStoreBehaviorHarness harness;

    protected OutboxStoreBehaviorTestsBase(IOutboxStoreBehaviorHarness harness)
    {
        this.harness = harness ?? throw new ArgumentNullException(nameof(harness));
    }

    protected IOutboxStoreBehaviorHarness Harness => harness;

    public ValueTask InitializeAsync() => harness.InitializeAsync();

    public ValueTask DisposeAsync() => harness.DisposeAsync();

    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        await harness.ResetAsync();

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimDueAsync_WithDueMessages_ReturnsMessages()
    {
        await harness.ResetAsync();

        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(1);
        messages[0].Topic.ShouldBe("Test.Topic");
        messages[0].Payload.ShouldBe("test payload");
        messages[0].IsProcessed.ShouldBeFalse();
    }

    [Fact]
    public async Task ClaimDueAsync_WithFutureMessages_ReturnsEmpty()
    {
        await harness.ResetAsync();

        var future = DateTimeOffset.UtcNow.AddMinutes(10);
        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", correlationId: null, dueTimeUtc: future, CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimDueAsync_ReturnsCorrelationIdAndDueTime()
    {
        await harness.ResetAsync();

        var dueTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var correlationId = $"corr-{Guid.NewGuid():N}";

        await harness.Outbox.EnqueueAsync("Test.Topic", "payload", correlationId, dueTime, CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(1);
        messages[0].CorrelationId.ShouldBe(correlationId);
        messages[0].DueTimeUtc.ShouldNotBeNull();
        messages[0].DueTimeUtc!.Value.UtcDateTime.ShouldBeInRange(
            dueTime.UtcDateTime.AddSeconds(-2),
            dueTime.UtcDateTime.AddSeconds(2));
    }

    [Fact]
    public async Task MarkDispatchedAsync_RemovesMessageFromClaims()
    {
        await harness.ResetAsync();

        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        messages.Count.ShouldBe(1);

        await harness.Store.MarkDispatchedAsync(messages[0].Id, CancellationToken.None);

        var remaining = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task RescheduleAsync_MakesMessageAvailableAgain()
    {
        await harness.ResetAsync();

        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        messages.Count.ShouldBe(1);

        const string errorMessage = "Test error";
        await harness.Store.RescheduleAsync(messages[0].Id, TimeSpan.Zero, errorMessage, CancellationToken.None);

        var rescheduled = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        rescheduled.Count.ShouldBe(1);
        rescheduled[0].RetryCount.ShouldBe(1);
        rescheduled[0].LastError.ShouldBe(errorMessage);
    }

    [Fact]
    public async Task FailAsync_RemovesMessageFromClaims()
    {
        await harness.ResetAsync();

        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        messages.Count.ShouldBe(1);

        await harness.Store.FailAsync(messages[0].Id, "Permanent failure", CancellationToken.None);

        var remaining = await harness.Store.ClaimDueAsync(10, CancellationToken.None);
        remaining.Count.ShouldBe(0);
    }
}
