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
using Bravellian.Platform;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Webhooks.Tests;

public sealed class WebhookProcessorTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);

    /// <summary>When successful Handling Marks Completed Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for successful Handling Marks Completed Async.</intent>
    /// <scenario>Given successful Handling Marks Completed Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task SuccessfulHandlingMarksCompletedAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var handler = new CountingWebhookHandler("invoice.paid");
        var provider = new FakeWebhookProvider(
            "stripe",
            new[] { handler });
        var registry = new WebhookProviderRegistry(new[] { provider });
        var processor = new WebhookProcessor(workStore, registry);

        var record = CreateRecord("stripe", "invoice.paid");
        workStore.AddMessage(BuildInboxPayload(record));

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        workStore.Acked.Count.ShouldBe(1);
        workStore.Abandoned.Count.ShouldBe(0);
        workStore.Failed.Count.ShouldBe(0);
        handler.Invocations.ShouldBe(1);
    }

    /// <summary>When transient Failure Schedules Retry Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for transient Failure Schedules Retry Async.</intent>
    /// <scenario>Given transient Failure Schedules Retry Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task TransientFailureSchedulesRetryAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var provider = new FakeWebhookProvider(
            "stripe",
            new[] { new FailingWebhookHandler("invoice.paid") });
        var registry = new WebhookProviderRegistry(new[] { provider });
        var options = new WebhookProcessorOptions
        {
            BaseBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromSeconds(10),
            MaxAttempts = 5,
        };
        var processor = new WebhookProcessor(workStore, registry, options);

        var record = CreateRecord("stripe", "invoice.paid");
        workStore.AddMessage(BuildInboxPayload(record));

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        workStore.Acked.Count.ShouldBe(0);
        workStore.Failed.Count.ShouldBe(0);
        workStore.Abandoned.Count.ShouldBe(1);
        workStore.Abandoned[0].Delay.ShouldBe(TimeSpan.FromSeconds(2));
    }

    /// <summary>When max Attempts Poisons Message Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for max Attempts Poisons Message Async.</intent>
    /// <scenario>Given max Attempts Poisons Message Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task MaxAttemptsPoisonsMessageAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var provider = new FakeWebhookProvider(
            "stripe",
            new[] { new FailingWebhookHandler("invoice.paid") });
        var registry = new WebhookProviderRegistry(new[] { provider });
        var options = new WebhookProcessorOptions
        {
            MaxAttempts = 2,
        };
        var processor = new WebhookProcessor(workStore, registry, options);

        var record = CreateRecord("stripe", "invoice.paid");
        workStore.AddMessage(BuildInboxPayload(record), initialAttempt: 2);

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        workStore.Failed.Count.ShouldBe(1);
        workStore.Abandoned.Count.ShouldBe(0);
    }

    /// <summary>When no Handler Marks Completed Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for no Handler Marks Completed Async.</intent>
    /// <scenario>Given no Handler Marks Completed Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task NoHandlerMarksCompletedAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var provider = new FakeWebhookProvider("stripe", Array.Empty<IWebhookHandler>());
        var registry = new WebhookProviderRegistry(new[] { provider });
        var processor = new WebhookProcessor(workStore, registry);

        var record = CreateRecord("stripe", "invoice.paid");
        workStore.AddMessage(BuildInboxPayload(record));

        var processed = await processor.RunOnceAsync(CancellationToken.None);

        processed.ShouldBe(1);
        workStore.Acked.Count.ShouldBe(1);
        workStore.Abandoned.Count.ShouldBe(0);
        workStore.Failed.Count.ShouldBe(0);
    }

    /// <summary>When processing Invokes Callback Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for processing Invokes Callback Async.</intent>
    /// <scenario>Given processing Invokes Callback Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ProcessingInvokesCallbackAsync()
    {
        var workStore = new FakeInboxWorkStore(new FixedTimeProvider(FixedNow));
        var handler = new CountingWebhookHandler("invoice.paid");
        var provider = new FakeWebhookProvider(
            "stripe",
            new[] { handler });
        var registry = new WebhookProviderRegistry(new[] { provider });
        var results = new List<ProcessingResult>();
        var webhookOptions = new WebhookOptions
        {
            OnProcessed = (result, _) => results.Add(result),
        };
        var processor = new WebhookProcessor(workStore, registry, null, webhookOptions);

        var record = CreateRecord("stripe", "invoice.paid");
        workStore.AddMessage(BuildInboxPayload(record));

        await processor.RunOnceAsync(CancellationToken.None);

        results.Count.ShouldBe(1);
        results[0].Status.ShouldBe(WebhookEventStatus.Completed);
    }

    private static WebhookEventRecord CreateRecord(string provider, string eventType)
    {
        return new WebhookEventRecord(
            provider,
            FixedNow,
            "evt_123",
            eventType,
            "dedupe-1",
            null,
            JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            "{\"id\":\"evt_123\"}"u8.ToArray(),
            "application/json",
            WebhookEventStatus.Pending,
            0,
            null);
    }

    private static string BuildInboxPayload(WebhookEventRecord record)
    {
        return JsonSerializer.Serialize(record);
    }

    private sealed class FakeWebhookProvider : IWebhookProvider
    {
        public FakeWebhookProvider(string name, IReadOnlyCollection<IWebhookHandler> handlers)
        {
            Name = name;
            Handlers = handlers.ToList();
            Authenticator = new FakeAuthenticator();
            Classifier = new FakeClassifier();
        }

        public string Name { get; }

        public IWebhookAuthenticator Authenticator { get; }

        public IWebhookClassifier Classifier { get; }

        public IReadOnlyList<IWebhookHandler> Handlers { get; }
    }

    private sealed class CountingWebhookHandler : IWebhookHandler
    {
        private readonly string eventType;
        private int invocations;

        public CountingWebhookHandler(string eventType)
        {
            this.eventType = eventType;
        }

        public int Invocations => invocations;

        public bool CanHandle(string eventType)
        {
            return string.Equals(this.eventType, eventType, StringComparison.OrdinalIgnoreCase);
        }

        public Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.EventType is null)
            {
                throw new InvalidOperationException("Event type missing.");
            }

            Interlocked.Increment(ref invocations);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingWebhookHandler : IWebhookHandler
    {
        private readonly string eventType;

        public FailingWebhookHandler(string eventType)
        {
            this.eventType = eventType;
        }

        public bool CanHandle(string eventType)
        {
            return string.Equals(this.eventType, eventType, StringComparison.OrdinalIgnoreCase);
        }

        public Task HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class FakeAuthenticator : IWebhookAuthenticator
    {
        public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthResult(true, null));
        }
    }

    private sealed class FakeClassifier : IWebhookClassifier
    {
        public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ClassifyResult(WebhookIngestDecision.Accepted, null, null, null, null, null, null));
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

        public List<string> Acked { get; } = new();

        public List<(string MessageId, TimeSpan Delay, string Error)> Abandoned { get; } = new();

        public List<(string MessageId, string Error)> Failed { get; } = new();

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
                Acked.Add(messageId);
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
                Abandoned.Add((messageId, effectiveDelay, lastError ?? string.Empty));
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
                Failed.Add((messageId, errorMessage));
                states[messageId] = WorkState.Poisoned;
            }

            return Task.CompletedTask;
        }

        public Task ReviveAsync(IEnumerable<string> messageIds, string? reason = null, TimeSpan? delay = null, CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow();
            var effectiveDelay = delay ?? TimeSpan.Zero;
            foreach (var messageId in messageIds)
            {
                if (!messages.TryGetValue(messageId, out var message))
                {
                    continue;
                }

                messages[messageId] = message with
                {
                    DueTimeUtc = effectiveDelay == TimeSpan.Zero ? null : now.Add(effectiveDelay),
                    LastError = string.IsNullOrEmpty(reason) ? message.LastError : reason,
                };
                states[messageId] = WorkState.Ready;
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

