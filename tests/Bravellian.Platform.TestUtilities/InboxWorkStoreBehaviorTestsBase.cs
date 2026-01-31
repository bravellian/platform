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
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests.TestUtilities;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test naming uses underscores for readability.")]
public abstract class InboxWorkStoreBehaviorTestsBase : IAsyncLifetime
{
    private readonly IInboxWorkStoreBehaviorHarness harness;

    protected InboxWorkStoreBehaviorTestsBase(IInboxWorkStoreBehaviorHarness harness)
    {
        this.harness = harness ?? throw new ArgumentNullException(nameof(harness));
    }

    protected IInboxWorkStoreBehaviorHarness Harness => harness;

    public ValueTask InitializeAsync() => harness.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await harness.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ClaimAsync_WithNoMessages_ReturnsEmptyList()
    {
        await harness.ResetAsync();

        var claimed = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimAsync_WithAvailableMessage_ReturnsMessageId()
    {
        await harness.ResetAsync();

        const string messageId = "msg-claim-1";
        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            CancellationToken.None);

        var claimed = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(1);
        claimed[0].ShouldBe(messageId);
    }

    [Fact]
    public async Task ClaimAsync_WithFutureDueTime_ReturnsEmpty()
    {
        await harness.ResetAsync();

        const string messageId = "msg-future-1";
        var dueTimeUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            hash: null,
            dueTimeUtc: dueTimeUtc,
            cancellationToken: CancellationToken.None);

        var claimed = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AckAsync_RemovesMessageFromClaims()
    {
        await harness.ResetAsync();

        const string messageId = "msg-ack-1";
        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            CancellationToken.None);

        var owner = OwnerToken.GenerateNew();
        var claimed = await harness.WorkStore.ClaimAsync(
            owner,
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(1);

        await harness.WorkStore.AckAsync(owner, claimed, CancellationToken.None);

        var remaining = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AbandonAsync_ReturnsMessageToQueue()
    {
        await harness.ResetAsync();

        const string messageId = "msg-abandon-1";
        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            CancellationToken.None);

        var owner = OwnerToken.GenerateNew();
        var claimed = await harness.WorkStore.ClaimAsync(
            owner,
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(1);

        await harness.WorkStore.AbandonAsync(
            owner,
            claimed,
            lastError: "transient",
            delay: TimeSpan.Zero,
            CancellationToken.None);

        var reClaimed = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        reClaimed.Count.ShouldBe(1);
        reClaimed[0].ShouldBe(messageId);
    }

    [Fact]
    public async Task FailAsync_RemovesMessageFromClaims()
    {
        await harness.ResetAsync();

        const string messageId = "msg-fail-1";
        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            CancellationToken.None);

        var owner = OwnerToken.GenerateNew();
        var claimed = await harness.WorkStore.ClaimAsync(
            owner,
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        claimed.Count.ShouldBe(1);

        await harness.WorkStore.FailAsync(owner, claimed, "permanent failure", CancellationToken.None);

        var remaining = await harness.WorkStore.ClaimAsync(
            OwnerToken.GenerateNew(),
            leaseSeconds: 30,
            batchSize: 10,
            CancellationToken.None);

        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetAsync_ReturnsStoredMessage()
    {
        await harness.ResetAsync();

        const string messageId = "msg-get-1";
        await harness.Inbox.EnqueueAsync(
            "test-topic",
            "test-source",
            messageId,
            "payload",
            CancellationToken.None);

        var message = await harness.WorkStore.GetAsync(messageId, CancellationToken.None);

        message.MessageId.ShouldBe(messageId);
        message.Source.ShouldBe("test-source");
        message.Topic.ShouldBe("test-topic");
        message.Payload.ShouldBe("payload");
    }
}
