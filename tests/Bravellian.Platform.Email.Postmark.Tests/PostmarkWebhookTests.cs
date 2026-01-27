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

using System.Reflection;
using System.Text.Json;
using Shouldly;
using Bravellian.Platform;
using Bravellian.Platform.Email;
using Bravellian.Platform.Email.Postmark;
using Bravellian.Platform.Observability;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Email.Postmark.Tests;

public sealed class PostmarkWebhookTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WebhookIngestorStoresPostmarkBounceAsync()
    {
        var inbox = new FakeInbox();
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var envelope = CreateEnvelope(CreateBouncePayload());
        var result = await ingestor.IngestAsync(PostmarkWebhookProvider.DefaultProviderName, envelope, CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Accepted);
        inbox.Enqueued.Count.ShouldBe(1);

        var record = JsonSerializer.Deserialize<WebhookEventRecord>(inbox.Enqueued[0].Payload);
        record.ShouldNotBeNull();
        record!.EventType.ShouldBe(PostmarkWebhookEventTypes.Bounce);
        record.ProviderEventId.ShouldBe("42");
    }

    [Fact]
    public async Task WebhookIngestorStoresPostmarkSpamComplaintAsync()
    {
        var inbox = new FakeInbox();
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var envelope = CreateEnvelope(CreateSpamComplaintPayload());
        var result = await ingestor.IngestAsync(PostmarkWebhookProvider.DefaultProviderName, envelope, CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Accepted);
        inbox.Enqueued.Count.ShouldBe(1);

        var record = JsonSerializer.Deserialize<WebhookEventRecord>(inbox.Enqueued[0].Payload);
        record.ShouldNotBeNull();
        record!.EventType.ShouldBe(PostmarkWebhookEventTypes.SpamComplaint);
        record.ProviderEventId.ShouldBe("spam-1");
    }

    [Fact]
    public async Task WebhookIngestorStoresPostmarkSubscriptionChangeAsync()
    {
        var inbox = new FakeInbox();
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var envelope = CreateEnvelope(CreateSubscriptionChangePayload());
        var result = await ingestor.IngestAsync(PostmarkWebhookProvider.DefaultProviderName, envelope, CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Accepted);
        inbox.Enqueued.Count.ShouldBe(1);

        var record = JsonSerializer.Deserialize<WebhookEventRecord>(inbox.Enqueued[0].Payload);
        record.ShouldNotBeNull();
        record!.EventType.ShouldBe(PostmarkWebhookEventTypes.SubscriptionChange);
        record.ProviderEventId.ShouldBe("sub-1");
    }

    [Fact]
    public async Task WebhookIngestorStoresPostmarkInboundAsync()
    {
        var inbox = new FakeInbox();
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var envelope = CreateEnvelope(CreateInboundPayload());
        var result = await ingestor.IngestAsync(PostmarkWebhookProvider.DefaultProviderName, envelope, CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Accepted);
        inbox.Enqueued.Count.ShouldBe(1);

        var record = JsonSerializer.Deserialize<WebhookEventRecord>(inbox.Enqueued[0].Payload);
        record.ShouldNotBeNull();
        record!.EventType.ShouldBe(PostmarkWebhookEventTypes.Inbound);
        record.ProviderEventId.ShouldBe("inb-1");
    }

    [Fact]
    public async Task WebhookProcessorDispatchesBounceToSinkAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var processor = new WebhookProcessor(workStore, registry);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(CreateBouncePayload());
        var record = new WebhookEventRecord(
            PostmarkWebhookProvider.DefaultProviderName,
            FixedNow,
            "evt-1",
            PostmarkWebhookEventTypes.Bounce,
            "dedupe-1",
            null,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            payloadBytes,
            "application/json",
            WebhookEventStatus.Pending,
            0,
            null);

        workStore.AddMessage(JsonSerializer.Serialize(record));

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        sink.ExternalUpdates.Count.ShouldBe(1);
        var update = sink.ExternalUpdates[0];
        update.Status.ShouldBe(EmailDeliveryStatus.Bounced);
        update.MessageKey.ShouldBe("message-key");
        update.ProviderMessageId.ShouldBe("pm_123");
        update.ProviderEventId.ShouldBe("evt-1");
        update.ErrorCode.ShouldBe("HardBounce");
        update.ErrorMessage.ShouldBe("Mailbox not found");
    }

    [Fact]
    public async Task WebhookProcessor_EmitsWebhookReceivedAuditEvent()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var sink = new TestEmailDeliverySink();
        var emitter = new RecordingEventEmitter();
        var provider = new PostmarkWebhookProvider(sink, emitter, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var processor = new WebhookProcessor(workStore, registry);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(CreateBouncePayload());
        var record = new WebhookEventRecord(
            PostmarkWebhookProvider.DefaultProviderName,
            FixedNow,
            "evt-2",
            PostmarkWebhookEventTypes.Bounce,
            "dedupe-2",
            null,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            payloadBytes,
            "application/json",
            WebhookEventStatus.Pending,
            0,
            null);

        workStore.AddMessage(JsonSerializer.Serialize(record));

        await processor.RunOnceAsync(CancellationToken.None);

        emitter.AuditEvents.ShouldContain(e => e.Name == PlatformEventNames.WebhookReceived);
    }

    [Fact]
    public async Task WebhookProcessorDispatchesSpamComplaintAsSuppressedAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var sink = new TestEmailDeliverySink();
        var provider = new PostmarkWebhookProvider(sink, new PostmarkWebhookOptions());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var processor = new WebhookProcessor(workStore, registry);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(CreateSpamComplaintPayload());
        var record = new WebhookEventRecord(
            PostmarkWebhookProvider.DefaultProviderName,
            FixedNow,
            "spam-evt-1",
            PostmarkWebhookEventTypes.SpamComplaint,
            "dedupe-2",
            null,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            payloadBytes,
            "application/json",
            WebhookEventStatus.Pending,
            0,
            null);

        workStore.AddMessage(JsonSerializer.Serialize(record));

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        sink.ExternalUpdates.Count.ShouldBe(1);
        sink.ExternalUpdates[0].Status.ShouldBe(EmailDeliveryStatus.Suppressed);
    }

    private static WebhookEnvelope CreateEnvelope(object payload)
    {
        return new WebhookEnvelope(
            PostmarkWebhookProvider.DefaultProviderName,
            FixedNow,
            "POST",
            "/webhooks/postmark",
            "",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "application/json",
            JsonSerializer.SerializeToUtf8Bytes(payload),
            "127.0.0.1");
    }

    private static object CreateBouncePayload()
    {
        return new
        {
            RecordType = "Bounce",
            ID = 42,
            MessageID = "pm_123",
            Type = "HardBounce",
            Description = "Mailbox not found",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MessageKey"] = "message-key",
            },
        };
    }

    private static object CreateSpamComplaintPayload()
    {
        return new
        {
            RecordType = "SpamComplaint",
            ID = "spam-1",
            MessageID = "pm_456",
            Type = "SpamComplaint",
            Description = "Marked as spam",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MessageKey"] = "message-key-spam",
            },
        };
    }

    private static object CreateSubscriptionChangePayload()
    {
        return new
        {
            RecordType = "SubscriptionChange",
            ID = "sub-1",
            MessageID = "pm_789",
            Type = "SubscriptionChange",
            Description = "Recipient unsubscribed",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MessageKey"] = "message-key-sub",
            },
        };
    }

    private static object CreateInboundPayload()
    {
        return new
        {
            RecordType = "Inbound",
            ID = "inb-1",
            MessageID = "pm_inbound_1",
            From = "sender@acme.test",
            Subject = "Reply",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MessageKey"] = "message-key-inbound",
            },
        };
    }

    private sealed class TestEmailDeliverySink : IEmailDeliverySink
    {
        public List<EmailDeliveryUpdate> ExternalUpdates { get; } = new();

        public Task RecordQueuedAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordAttemptAsync(OutboundEmailMessage message, EmailDeliveryAttempt attempt, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordFinalAsync(
            OutboundEmailMessage message,
            EmailDeliveryStatus status,
            string? providerMessageId,
            string? errorCode,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordExternalAsync(EmailDeliveryUpdate update, CancellationToken cancellationToken)
        {
            ExternalUpdates.Add(update);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventEmitter : IPlatformEventEmitter
    {
        public List<Bravellian.Platform.Audit.AuditEvent> AuditEvents { get; } = new();

        public Task<Bravellian.Platform.Operations.OperationId> EmitOperationStartedAsync(
            string name,
            Bravellian.Platform.Correlation.CorrelationContext? correlationContext,
            Bravellian.Platform.Operations.OperationId? parentOperationId,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Bravellian.Platform.Operations.OperationId.NewId());
        }

        public Task EmitOperationCompletedAsync(
            Bravellian.Platform.Operations.OperationId operationId,
            Bravellian.Platform.Operations.OperationStatus status,
            string? message,
            Bravellian.Platform.Correlation.CorrelationContext? correlationContext,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EmitAuditEventAsync(Bravellian.Platform.Audit.AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            AuditEvents.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset fixedUtcNow;

        public FixedTimeProvider(DateTimeOffset fixedUtcNow)
        {
            this.fixedUtcNow = fixedUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }

    private sealed class FakeInbox : IInbox
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);

        public List<EnqueuedMessage> Enqueued { get; } = new();

        public Task<bool> AlreadyProcessedAsync(string messageId, string source, CancellationToken cancellationToken)
        {
            return Task.FromResult(!seen.Add(BuildKey(messageId, source)));
        }

        public Task<bool> AlreadyProcessedAsync(string messageId, string source, byte[]? hash, CancellationToken cancellationToken)
        {
            return Task.FromResult(!seen.Add(BuildKey(messageId, source)));
        }

        public Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task MarkProcessingAsync(string messageId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task MarkDeadAsync(string messageId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string source, string messageId, string payload, CancellationToken cancellationToken)
        {
            Enqueued.Add(new EnqueuedMessage(topic, source, messageId, payload));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string source, string messageId, string payload, byte[]? hash, CancellationToken cancellationToken)
        {
            Enqueued.Add(new EnqueuedMessage(topic, source, messageId, payload));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(
            string topic,
            string source,
            string messageId,
            string payload,
            byte[]? hash,
            DateTimeOffset? dueTimeUtc,
            CancellationToken cancellationToken)
        {
            Enqueued.Add(new EnqueuedMessage(topic, source, messageId, payload));
            return Task.CompletedTask;
        }

        private static string BuildKey(string messageId, string source) => $"{source}:{messageId}";
    }

    private sealed record EnqueuedMessage(string Topic, string Source, string MessageId, string Payload);

    private sealed class FakeInboxWorkStore : IInboxWorkStore
    {
        private readonly TimeProvider timeProvider;
        private int messageSequence;
        private readonly Dictionary<string, StoredMessage> messages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkState> states = new(StringComparer.Ordinal);

        public FakeInboxWorkStore(TimeProvider timeProvider)
        {
            this.timeProvider = timeProvider;
        }

        public void AddMessage(string payload, int initialAttempt = 0)
        {
            var messageId = $"msg-{Interlocked.Increment(ref messageSequence):D4}";
            var now = timeProvider.GetUtcNow();
            messages[messageId] = new StoredMessage(
                messageId,
                "webhooks",
                "webhook",
                payload,
                null,
                initialAttempt,
                now,
                now,
                null,
                null);
            states[messageId] = WorkState.Ready;
        }

        public Task<IReadOnlyList<string>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();
            var ready = messages.Values
                .Where(message => states[message.MessageId] == WorkState.Ready)
                .Where(message => message.DueTimeUtc == null || message.DueTimeUtc <= now)
                .Take(batchSize)
                .Select(message => message.MessageId)
                .ToList();

            foreach (var messageId in ready)
            {
                var message = messages[messageId];
                messages[messageId] = message with
                {
                    Attempt = message.Attempt + 1,
                    LastSeenUtc = now,
                };
                states[messageId] = WorkState.Processing;
            }

            return Task.FromResult<IReadOnlyList<string>>(ready);
        }

        public Task AckAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken)
        {
            foreach (var messageId in messageIds)
            {
                states[messageId] = WorkState.Completed;
            }

            return Task.CompletedTask;
        }

        public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow();
            var effectiveDelay = delay ?? TimeSpan.Zero;
            foreach (var messageId in messageIds)
            {
                var message = messages[messageId];
                messages[messageId] = message with
                {
                    DueTimeUtc = now.Add(effectiveDelay),
                    LastError = lastError,
                };
                states[messageId] = WorkState.Ready;
            }

            return Task.CompletedTask;
        }

        public Task FailAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string errorMessage, CancellationToken cancellationToken)
        {
            foreach (var messageId in messageIds)
            {
                states[messageId] = WorkState.Poisoned;
            }

            return Task.CompletedTask;
        }

        public Task ReapExpiredAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
        {
            var message = messages[messageId];
            return Task.FromResult(CreateInboxMessage(message));
        }

        private static InboxMessage CreateInboxMessage(StoredMessage message)
        {
            var instance = new InboxMessage();
            SetProperty(instance, nameof(InboxMessage.MessageId), message.MessageId);
            SetProperty(instance, nameof(InboxMessage.Source), message.Source);
            SetProperty(instance, nameof(InboxMessage.Topic), message.Topic);
            SetProperty(instance, nameof(InboxMessage.Payload), message.Payload);
            SetProperty(instance, nameof(InboxMessage.Hash), message.Hash);
            SetProperty(instance, nameof(InboxMessage.Attempt), message.Attempt);
            SetProperty(instance, nameof(InboxMessage.FirstSeenUtc), message.FirstSeenUtc);
            SetProperty(instance, nameof(InboxMessage.LastSeenUtc), message.LastSeenUtc);
            SetProperty(instance, nameof(InboxMessage.DueTimeUtc), message.DueTimeUtc);
            SetProperty(instance, nameof(InboxMessage.LastError), message.LastError);
            return instance;
        }

        private static void SetProperty<T>(InboxMessage instance, string name, T value)
        {
            var prop = typeof(InboxMessage).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
            {
                throw new InvalidOperationException($"Property '{name}' not found.");
            }

            prop.SetValue(instance, value);
        }

        private sealed record StoredMessage(
            string MessageId,
            string Source,
            string Topic,
            string Payload,
            byte[]? Hash,
            int Attempt,
            DateTimeOffset FirstSeenUtc,
            DateTimeOffset LastSeenUtc,
            DateTimeOffset? DueTimeUtc,
            string? LastError);

        private enum WorkState
        {
            Ready,
            Processing,
            Completed,
            Poisoned,
        }
    }
}


