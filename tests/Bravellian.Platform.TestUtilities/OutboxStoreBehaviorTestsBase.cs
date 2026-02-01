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

using System;
using System.Diagnostics.CodeAnalysis;
using Bravellian.Platform.Outbox;
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests.TestUtilities;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test naming uses underscores for readability.")]
public abstract class OutboxStoreBehaviorTestsBase : IAsyncLifetime
{
    private readonly IOutboxStoreBehaviorHarness harness;

    protected OutboxStoreBehaviorTestsBase(IOutboxStoreBehaviorHarness harness)
    {
        this.harness = harness ?? throw new ArgumentNullException(nameof(harness));
    }

    protected IOutboxStoreBehaviorHarness Harness => harness;

    public ValueTask InitializeAsync() => harness.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await harness.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>When claim Due Async With No Messages Returns Empty List, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for claim Due Async With No Messages Returns Empty List.</intent>
    /// <scenario>Given claim Due Async With No Messages Returns Empty List.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        await harness.ResetAsync();

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    /// <summary>When claim Due Async With Due Messages Returns Messages, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for claim Due Async With Due Messages Returns Messages.</intent>
    /// <scenario>Given claim Due Async With Due Messages Returns Messages.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When claim Due Async With Future Messages Returns Empty, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for claim Due Async With Future Messages Returns Empty.</intent>
    /// <scenario>Given claim Due Async With Future Messages Returns Empty.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ClaimDueAsync_WithFutureMessages_ReturnsEmpty()
    {
        await harness.ResetAsync();

        var future = DateTimeOffset.UtcNow.AddMinutes(10);
        await harness.Outbox.EnqueueAsync("Test.Topic", "test payload", correlationId: null, dueTimeUtc: future, CancellationToken.None);

        var messages = await harness.Store.ClaimDueAsync(10, CancellationToken.None);

        messages.Count.ShouldBe(0);
    }

    /// <summary>When claim Due Async Returns Correlation Id And Due Time, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for claim Due Async Returns Correlation Id And Due Time.</intent>
    /// <scenario>Given claim Due Async Returns Correlation Id And Due Time.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When mark Dispatched Async Removes Message From Claims, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for mark Dispatched Async Removes Message From Claims.</intent>
    /// <scenario>Given mark Dispatched Async Removes Message From Claims.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When reschedule Async Makes Message Available Again, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for reschedule Async Makes Message Available Again.</intent>
    /// <scenario>Given reschedule Async Makes Message Available Again.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When fail Async Removes Message From Claims, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for fail Async Removes Message From Claims.</intent>
    /// <scenario>Given fail Async Removes Message From Claims.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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
