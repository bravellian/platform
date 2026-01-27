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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bravellian.Platform.Email.Tests;

public sealed class EmailOutboxOrchestrationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public async Task Enqueue_ValidatesAndRecordsQueued()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var outbox = new EmailOutbox(harness, sink);
        var message = EmailFixtures.CreateMessage(messageKey: "key-1");

        await outbox.EnqueueAsync(message, CancellationToken.None);

        harness.EnqueuedCount.ShouldBe(1);
        sink.Queued.Count.ShouldBe(1);
        sink.Queued[0].MessageKey.ShouldBe("key-1");
    }

    [Fact]
    public async Task Processor_SendsOnceAndMarksSent()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.Success("msg-1"));
        var idempotency = new InMemoryIdempotencyStore();
        var processor = new EmailOutboxProcessor(harness, sender, idempotency, sink, timeProvider: timeProvider);
        var message = EmailFixtures.CreateMessage(messageKey: "key-2");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(message), message.MessageKey, null, CancellationToken.None);

        var processed = await processor.ProcessOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        sender.SendCount.ShouldBe(1);
        harness.DispatchedCount.ShouldBe(1);
        sink.Attempts.Count.ShouldBe(1);
        sink.Attempts[0].Status.ShouldBe(EmailDeliveryStatus.Sent);
        sink.Final.Single().Status.ShouldBe(EmailDeliveryStatus.Sent);
    }

    [Fact]
    public async Task DuplicateEnqueue_DoesNotCreateSecondSend()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.Success("msg-1"));
        var idempotency = new InMemoryIdempotencyStore();
        var processor = new EmailOutboxProcessor(harness, sender, idempotency, sink, timeProvider: timeProvider);
        var message = EmailFixtures.CreateMessage(messageKey: "key-dup");
        var payload = Serialize(message);

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, payload, message.MessageKey, null, CancellationToken.None);
        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, payload, message.MessageKey, null, CancellationToken.None);

        var processed = await processor.ProcessOnceAsync(CancellationToken.None);

        processed.ShouldBe(2);
        sender.SendCount.ShouldBe(1);
        harness.DispatchedCount.ShouldBe(2);
        sink.Final.Count.ShouldBe(2);
        sink.Final.Any(entry => entry.Status == EmailDeliveryStatus.Sent).ShouldBeTrue();
        sink.Final.Any(entry => entry.Status == EmailDeliveryStatus.Suppressed).ShouldBeTrue();
    }

    [Fact]
    public async Task TransientFailure_RetriesAndEventuallySucceeds()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(
            EmailSendResult.TransientFailure("timeout", "timeout"),
            EmailSendResult.Success("msg-2"));
        var idempotency = new InMemoryIdempotencyStore();
        var options = new EmailOutboxProcessorOptions
        {
            BackoffPolicy = _ => TimeSpan.FromMinutes(1)
        };
        var processor = new EmailOutboxProcessor(harness, sender, idempotency, sink, timeProvider: timeProvider, options: options);
        var message = EmailFixtures.CreateMessage(messageKey: "key-retry");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(message), message.MessageKey, null, CancellationToken.None);

        await processor.ProcessOnceAsync(CancellationToken.None);

        sender.SendCount.ShouldBe(1);
        harness.DispatchedCount.ShouldBe(0);
        sink.Attempts.Last().Status.ShouldBe(EmailDeliveryStatus.FailedTransient);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessOnceAsync(CancellationToken.None);

        sender.SendCount.ShouldBe(2);
        harness.DispatchedCount.ShouldBe(1);
        sink.Final.Last().Status.ShouldBe(EmailDeliveryStatus.Sent);
    }

    [Fact]
    public async Task PermanentFailure_StopsAndMarksFinal()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.PermanentFailure("boom", "boom"));
        var idempotency = new InMemoryIdempotencyStore();
        var processor = new EmailOutboxProcessor(harness, sender, idempotency, sink, timeProvider: timeProvider);
        var message = EmailFixtures.CreateMessage(messageKey: "key-fail");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(message), message.MessageKey, null, CancellationToken.None);

        await processor.ProcessOnceAsync(CancellationToken.None);

        sender.SendCount.ShouldBe(1);
        harness.FailedCount.ShouldBe(1);
        sink.Final.Last().Status.ShouldBe(EmailDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task PolicyDelay_ReschedulesWithoutSending()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.Success("msg-1"));
        var idempotency = new InMemoryIdempotencyStore();
        var policy = new FixedRateLimitPolicy(1, TimeSpan.FromMinutes(1), perRecipient: false, timeProvider: timeProvider);
        var processor = new EmailOutboxProcessor(
            harness,
            sender,
            idempotency,
            sink,
            policy,
            timeProvider);
        var first = EmailFixtures.CreateMessage(messageKey: "key-delay-1");
        var second = EmailFixtures.CreateMessage(messageKey: "key-delay-2");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(first), first.MessageKey, null, CancellationToken.None);
        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(second), second.MessageKey, null, CancellationToken.None);

        await processor.ProcessOnceAsync(CancellationToken.None);

        sender.SendCount.ShouldBe(1);
        harness.DispatchedCount.ShouldBe(1);
        harness.FailedCount.ShouldBe(0);
        sink.Attempts.Any(attempt => attempt.Status == EmailDeliveryStatus.Queued).ShouldBeTrue();
    }

    [Fact]
    public async Task PolicyReject_FinalizesWithoutSending()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.Success("msg-1"));
        var idempotency = new InMemoryIdempotencyStore();
        var policy = new RejectAllPolicy();
        var processor = new EmailOutboxProcessor(
            harness,
            sender,
            idempotency,
            sink,
            policy,
            timeProvider);
        var message = EmailFixtures.CreateMessage(messageKey: "key-reject");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, Serialize(message), message.MessageKey, null, CancellationToken.None);

        await processor.ProcessOnceAsync(CancellationToken.None);

        sender.SendCount.ShouldBe(0);
        harness.FailedCount.ShouldBe(1);
        sink.Final.Last().Status.ShouldBe(EmailDeliveryStatus.FailedPermanent);
    }

    private static string Serialize(OutboundEmailMessage message)
    {
        return JsonSerializer.Serialize(message, SerializerOptions);
    }

    private sealed class StubEmailSender : IOutboundEmailSender
    {
        private readonly Queue<EmailSendResult> results;

        public StubEmailSender(params EmailSendResult[] results)
        {
            this.results = new Queue<EmailSendResult>(results);
        }

        public int SendCount { get; private set; }

        public Task<EmailSendResult> SendAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class RejectAllPolicy : IEmailSendPolicy
    {
        public Task<PolicyDecision> EvaluateAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(PolicyDecision.Reject("Rejected by policy."));
        }
    }
}
