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
using Bravellian.Platform.Audit;
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Observability;
using Bravellian.Platform.Operations;
using Shouldly;

namespace Bravellian.Platform.Email.Tests;

public sealed class EmailObservabilityTests
{
    [Fact]
    public async Task Enqueue_EmitsQueuedAuditEvent()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var emitter = new RecordingEventEmitter();
        var outbox = new EmailOutbox(harness, sink, emitter);
        var message = EmailFixtures.CreateMessage(messageKey: "obs-queued");

        await outbox.EnqueueAsync(message, CancellationToken.None);

        emitter.AuditEvents.ShouldContain(e => e.Name == PlatformEventNames.EmailQueued);
        var payload = JsonDocument.Parse(emitter.AuditEvents.Last().DataJson ?? "{}");
        payload.RootElement.GetProperty(PlatformTagKeys.MessageKey).GetString().ShouldBe("obs-queued");
    }

    [Fact]
    public async Task Processor_EmitsAttemptAndSentAuditEvents()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var harness = new InMemoryOutboxHarness(timeProvider);
        var sink = new TestEmailDeliverySink();
        var sender = new StubEmailSender(EmailSendResult.Success("msg-obs"));
        var idempotency = new InMemoryIdempotencyStore();
        var emitter = new RecordingEventEmitter();
        var processor = new EmailOutboxProcessor(harness, sender, idempotency, sink, eventEmitter: emitter, timeProvider: timeProvider);
        var message = EmailFixtures.CreateMessage(messageKey: "obs-sent");

        await harness.EnqueueAsync(EmailOutboxDefaults.Topic, JsonSerializer.Serialize(message), message.MessageKey, null, CancellationToken.None);

        await processor.ProcessOnceAsync(CancellationToken.None);

        emitter.AuditEvents.ShouldContain(e => e.Name == PlatformEventNames.EmailAttempted);
        emitter.AuditEvents.ShouldContain(e => e.Name == PlatformEventNames.EmailSent);
    }

    private sealed class RecordingEventEmitter : IPlatformEventEmitter
    {
        public List<AuditEvent> AuditEvents { get; } = new();

        public Task<OperationId> EmitOperationStartedAsync(
            string name,
            CorrelationContext? correlationContext,
            OperationId? parentOperationId,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationId.NewId());
        }

        public Task EmitOperationCompletedAsync(
            OperationId operationId,
            OperationStatus status,
            string? message,
            CorrelationContext? correlationContext,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EmitAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            AuditEvents.Add(auditEvent);
            return Task.CompletedTask;
        }
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
