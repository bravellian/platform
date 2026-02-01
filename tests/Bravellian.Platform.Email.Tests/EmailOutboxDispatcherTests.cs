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

namespace Bravellian.Platform.Email.Tests;

public sealed class EmailOutboxDispatcherTests
{
    /// <summary>When dispatch Async Sends And Tracks Outcomes, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for dispatch Async Sends And Tracks Outcomes.</intent>
    /// <scenario>Given dispatch Async Sends And Tracks Outcomes.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task DispatchAsync_SendsAndTracksOutcomes()
    {
        var store = new InMemoryEmailOutboxStore();
        var sender = new StubEmailSender(
            EmailSendResult.Success("message-1"),
            EmailSendResult.TransientFailure("timeout", "timeout"));
        var dispatcher = new EmailOutboxDispatcher(store, sender);

        var firstMessage = EmailFixtures.CreateMessage("Hello 1", messageKey: "key-1");
        var secondMessage = EmailFixtures.CreateMessage("Hello 2", messageKey: "key-2");

        var firstItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            firstMessage.MessageKey,
            firstMessage,
            DateTimeOffset.UtcNow,
            null,
            0);
        var secondItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            secondMessage.MessageKey,
            secondMessage,
            DateTimeOffset.UtcNow,
            null,
            0);

        await store.EnqueueAsync(firstItem, CancellationToken.None);
        await store.EnqueueAsync(secondItem, CancellationToken.None);

        var result = await dispatcher.DispatchAsync(10, CancellationToken.None);

        result.AttemptedCount.ShouldBe(2);
        result.SucceededCount.ShouldBe(1);
        result.FailedCount.ShouldBe(1);
        result.TransientFailureCount.ShouldBe(1);

        store.TryGetEntry(firstItem.Id, out var firstStatus, out _, out var firstAttempts).ShouldBeTrue();
        firstStatus.ShouldBe(EmailOutboxStatus.Succeeded);
        firstAttempts.ShouldBe(1);

        store.TryGetEntry(secondItem.Id, out var secondStatus, out var failureReason, out var secondAttempts).ShouldBeTrue();
        secondStatus.ShouldBe(EmailOutboxStatus.Failed);
        failureReason.ShouldBe("timeout");
        secondAttempts.ShouldBe(1);
    }

    private sealed class StubEmailSender : IOutboundEmailSender
    {
        private readonly Queue<EmailSendResult> results;

        public StubEmailSender(params EmailSendResult[] results)
        {
            this.results = new Queue<EmailSendResult>(results);
        }

        public Task<EmailSendResult> SendAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(results.Dequeue());
        }
    }
}
