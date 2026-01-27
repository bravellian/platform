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

using System.Net;
using System.Text.Json;
using Bravellian.Platform;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Webhooks.Tests;

public sealed class WebhookIngestorTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthFailureReturnsRejectedAndDoesNotStoreAsync()
    {
        var inbox = new FakeInbox();
        var provider = new FakeWebhookProvider(
            "stripe",
            new FakeAuthenticator(false, "bad signature"),
            new FakeClassifier(WebhookIngestDecision.Accepted));
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var result = await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Rejected);
        result.HttpStatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        inbox.Enqueued.Count.ShouldBe(0);
    }

    [Fact]
    public async Task IgnoredDoesNotStoreAsync()
    {
        var inbox = new FakeInbox();
        var provider = new FakeWebhookProvider(
            "stripe",
            new FakeAuthenticator(true),
            new FakeClassifier(WebhookIngestDecision.Ignored));
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var result = await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Ignored);
        result.HttpStatusCode.ShouldBe(HttpStatusCode.Accepted);
        inbox.Enqueued.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AcceptedStoresExactlyOnceAsync()
    {
        var inbox = new FakeInbox();
        var provider = new FakeWebhookProvider(
            "stripe",
            new FakeAuthenticator(true),
            new FakeClassifier(WebhookIngestDecision.Accepted, dedupeKey: "dedupe-1", eventType: "invoice.paid"));
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var result = await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);

        result.Decision.ShouldBe(WebhookIngestDecision.Accepted);
        inbox.Enqueued.Count.ShouldBe(1);
        inbox.Enqueued[0].MessageId.ShouldBe("dedupe-1");

        var record = JsonSerializer.Deserialize<WebhookEventRecord>(inbox.Enqueued[0].Payload);
        record.ShouldNotBeNull();
        record!.DedupeKey.ShouldBe("dedupe-1");
        record.EventType.ShouldBe("invoice.paid");
        record.Status.ShouldBe(WebhookEventStatus.Pending);
    }

    [Fact]
    public async Task DuplicateDedupeDoesNotStoreAgainAsync()
    {
        var inbox = new FakeInbox();
        var provider = new FakeWebhookProvider(
            "stripe",
            new FakeAuthenticator(true),
            new FakeClassifier(WebhookIngestDecision.Accepted, dedupeKey: "dedupe-1", eventType: "invoice.paid"));
        var registry = new WebhookProviderRegistry(new[] { provider });
        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow));

        var first = await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);
        var second = await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);

        first.Duplicate.ShouldBeFalse();
        second.Duplicate.ShouldBeTrue();
        inbox.Enqueued.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CallbacksInvokedForRejectedAsync()
    {
        var inbox = new FakeInbox();
        var provider = new FakeWebhookProvider(
            "stripe",
            new FakeAuthenticator(false, "bad signature"),
            new FakeClassifier(WebhookIngestDecision.Accepted));
        var registry = new WebhookProviderRegistry(new[] { provider });

        WebhookIngestResult? ingested = null;
        string? rejectedReason = null;
        WebhookEnvelope? rejectedEnvelope = null;
        WebhookIngestResult? rejectedResult = null;

        var options = new WebhookOptions
        {
            OnIngested = (result, envelope) => ingested = result,
            OnRejected = (reason, envelope, result) =>
            {
                rejectedReason = reason;
                rejectedEnvelope = envelope;
                rejectedResult = result;
            },
        };

        var ingestor = new WebhookIngestor(registry, inbox, new FixedTimeProvider(FixedNow), options);

        await ingestor.IngestAsync("stripe", CreateEnvelope("stripe"), CancellationToken.None);

        ingested.ShouldNotBeNull();
        ingested!.Decision.ShouldBe(WebhookIngestDecision.Rejected);
        rejectedReason.ShouldBe("bad signature");
        rejectedEnvelope.ShouldNotBeNull();
        rejectedResult.ShouldNotBeNull();
    }

    private static WebhookEnvelope CreateEnvelope(string provider)
    {
        return new WebhookEnvelope(
            provider,
            FixedNow,
            "POST",
            "/webhooks/stripe",
            "",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Test"] = "value",
            },
            "application/json",
            "{\"id\":\"evt_123\"}"u8.ToArray(),
            "127.0.0.1");
    }

    private sealed class FakeWebhookProvider : IWebhookProvider
    {
        public FakeWebhookProvider(string name, IWebhookAuthenticator authenticator, IWebhookClassifier classifier)
        {
            Name = name;
            Authenticator = authenticator;
            Classifier = classifier;
            Handlers = Array.Empty<IWebhookHandler>();
        }

        public string Name { get; }

        public IWebhookAuthenticator Authenticator { get; }

        public IWebhookClassifier Classifier { get; }

        public IReadOnlyCollection<IWebhookHandler> Handlers { get; }
    }

    private sealed class FakeAuthenticator : IWebhookAuthenticator
    {
        private readonly AuthResult result;

        public FakeAuthenticator(bool isAuthenticated, string? failureReason = null)
        {
            result = new AuthResult(isAuthenticated, failureReason);
        }

        public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FakeClassifier : IWebhookClassifier
    {
        private readonly ClassifyResult result;

        public FakeClassifier(
            WebhookIngestDecision decision,
            string? providerEventId = null,
            string? eventType = null,
            string? dedupeKey = null,
            string? partitionKey = null,
            string? parsedSummaryJson = null,
            string? failureReason = null)
        {
            result = new ClassifyResult(
                decision,
                providerEventId,
                eventType,
                dedupeKey,
                partitionKey,
                parsedSummaryJson,
                failureReason);
        }

        public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
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

        public Task EnqueueAsync(string topic, string source, string messageId, string payload, byte[]? hash, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
        {
            Enqueued.Add(new EnqueuedMessage(topic, source, messageId, payload));
            return Task.CompletedTask;
        }

        private static string BuildKey(string messageId, string source)
        {
            return $"{source}:{messageId}";
        }
    }

    private sealed record EnqueuedMessage(string Topic, string Source, string MessageId, string Payload);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset fixedUtcNow;

        public FixedTimeProvider(DateTimeOffset fixedUtcNow)
        {
            this.fixedUtcNow = fixedUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }
}
