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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Default implementation of <see cref="IWebhookIngestor"/>.
/// </summary>
public sealed class WebhookIngestor : IWebhookIngestor
{
    private const string DefaultTopic = "webhook";
    private const string RejectedTopic = "webhook.rejected";

    private readonly IWebhookProviderRegistry providerRegistry;
    private readonly IInbox inbox;
    private readonly IInboxRouter? inboxRouter;
    private readonly IWebhookPartitionResolver? partitionResolver;
    private readonly WebhookOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookIngestor"/> class.
    /// </summary>
    /// <param name="providerRegistry">Provider registry.</param>
    /// <param name="inbox">Inbox instance for storing accepted webhooks.</param>
    /// <param name="timeProvider">Time provider for deterministic scheduling.</param>
    /// <param name="options">Webhook ingestion options.</param>
    /// <param name="inboxRouter">Optional inbox router for partitioned storage.</param>
    /// <param name="partitionResolver">Optional partition resolver.</param>
    public WebhookIngestor(
        IWebhookProviderRegistry providerRegistry,
        IInbox inbox,
        TimeProvider? timeProvider = null,
        WebhookOptions? options = null,
        IInboxRouter? inboxRouter = null,
        IWebhookPartitionResolver? partitionResolver = null)
    {
        this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        this.inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.options = options ?? new WebhookOptions();
        this.inboxRouter = inboxRouter;
        this.partitionResolver = partitionResolver;
    }

    /// <inheritdoc />
    public async Task<WebhookIngestResult> IngestAsync(string providerName, WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        ArgumentNullException.ThrowIfNull(envelope);
        WebhookMetrics.RecordReceived(providerName);

        var provider = providerRegistry.Get(providerName) ?? throw new InvalidOperationException($"No webhook provider registered for '{providerName}'.");

        var authResult = await provider.Authenticator.AuthenticateAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (!authResult.IsAuthenticated)
        {
            var rejection = new WebhookIngestResult(
                WebhookIngestDecision.Rejected,
                HttpStatusCode.Unauthorized,
                authResult.FailureReason,
                null,
                null,
                null,
                null,
                null,
                false);

            if (options.StoreRejected)
            {
                await StoreRejectedAsync(providerName, envelope, null, null, null, authResult.FailureReason, cancellationToken).ConfigureAwait(false);
            }

            WebhookMetrics.RecordRejected(providerName, "auth");
            options.OnRejected?.Invoke(authResult.FailureReason, envelope, rejection);
            options.OnIngested?.Invoke(rejection, envelope);
            return rejection;
        }

        var classifyResult = await provider.Classifier.ClassifyAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (classifyResult.Decision == WebhookIngestDecision.Ignored)
        {
            var ignored = new WebhookIngestResult(
                WebhookIngestDecision.Ignored,
                HttpStatusCode.Accepted,
                classifyResult.FailureReason,
                classifyResult.ProviderEventId,
                classifyResult.EventType,
                classifyResult.DedupeKey,
                classifyResult.PartitionKey,
                classifyResult.ParsedSummaryJson,
                false);
            WebhookMetrics.RecordRejected(providerName, "ignored");
            options.OnIngested?.Invoke(ignored, envelope);
            return ignored;
        }

        if (classifyResult.Decision == WebhookIngestDecision.Rejected)
        {
            var rejection = new WebhookIngestResult(
                WebhookIngestDecision.Rejected,
                HttpStatusCode.Forbidden,
                classifyResult.FailureReason,
                classifyResult.ProviderEventId,
                classifyResult.EventType,
                classifyResult.DedupeKey,
                classifyResult.PartitionKey,
                classifyResult.ParsedSummaryJson,
                false);

            if (options.StoreRejected)
            {
                await StoreRejectedAsync(
                    providerName,
                    envelope,
                    classifyResult.ProviderEventId,
                    classifyResult.EventType,
                    classifyResult.PartitionKey,
                    classifyResult.FailureReason,
                    cancellationToken).ConfigureAwait(false);
            }

            WebhookMetrics.RecordRejected(providerName, "classify");
            options.OnRejected?.Invoke(classifyResult.FailureReason, envelope, rejection);
            options.OnIngested?.Invoke(rejection, envelope);
            return rejection;
        }

        var partitionKey = await ResolvePartitionKeyAsync(envelope, classifyResult.PartitionKey, cancellationToken).ConfigureAwait(false);
        var dedupeKey = ResolveDedupeKey(providerName, classifyResult, envelope.BodyBytes);
        var headersJson = JsonSerializer.Serialize(envelope.Headers);
        var record = new WebhookEventRecord(
            providerName,
            envelope.ReceivedAtUtc,
            classifyResult.ProviderEventId,
            classifyResult.EventType,
            dedupeKey,
            partitionKey,
            headersJson,
            envelope.BodyBytes,
            envelope.ContentType,
            WebhookEventStatus.Pending,
            0,
            null);

        var targetInbox = GetInbox(partitionKey);
        var duplicate = await targetInbox.AlreadyProcessedAsync(dedupeKey, providerName, cancellationToken).ConfigureAwait(false);
        if (!duplicate)
        {
            var payloadJson = JsonSerializer.Serialize(record);
            var topic = string.IsNullOrWhiteSpace(classifyResult.EventType) ? DefaultTopic : classifyResult.EventType;
            await targetInbox.EnqueueAsync(topic, providerName, dedupeKey, payloadJson, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            WebhookMetrics.RecordDuplicate(providerName);
        }

        var accepted = new WebhookIngestResult(
            WebhookIngestDecision.Accepted,
            HttpStatusCode.Accepted,
            classifyResult.FailureReason,
            classifyResult.ProviderEventId,
            classifyResult.EventType,
            dedupeKey,
            partitionKey,
            classifyResult.ParsedSummaryJson,
            duplicate);
        WebhookMetrics.RecordAccepted(providerName);
        options.OnIngested?.Invoke(accepted, envelope);
        return accepted;
    }

    private async Task StoreRejectedAsync(
        string providerName,
        WebhookEnvelope envelope,
        string? providerEventId,
        string? eventType,
        string? partitionKey,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var resolvedPartitionKey = await ResolvePartitionKeyAsync(envelope, partitionKey, cancellationToken).ConfigureAwait(false);
        var bodyBytes = options.RedactRejectedBody ? Array.Empty<byte>() : envelope.BodyBytes;
        var contentType = options.RedactRejectedBody ? null : envelope.ContentType;
        var headersJson = JsonSerializer.Serialize(envelope.Headers);
        var dedupeKey = WebhookDedupe.Create(providerName, providerEventId, envelope.BodyBytes).Key;
        var record = new WebhookEventRecord(
            providerName,
            envelope.ReceivedAtUtc,
            providerEventId,
            eventType,
            dedupeKey,
            resolvedPartitionKey,
            headersJson,
            bodyBytes,
            contentType,
            WebhookEventStatus.Rejected,
            0,
            null);

        var targetInbox = GetInbox(resolvedPartitionKey);
        var duplicate = await targetInbox.AlreadyProcessedAsync(dedupeKey, providerName, cancellationToken).ConfigureAwait(false);
        if (duplicate)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(record);
        var dueTimeUtc = GetNeverDueTimeUtc();
        await targetInbox.EnqueueAsync(RejectedTopic, providerName, dedupeKey, payloadJson, null, dueTimeUtc, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveDedupeKey(string providerName, ClassifyResult classifyResult, byte[] bodyBytes)
    {
        if (!string.IsNullOrWhiteSpace(classifyResult.DedupeKey))
        {
            return classifyResult.DedupeKey;
        }

        return WebhookDedupe.Create(providerName, classifyResult.ProviderEventId, bodyBytes).Key;
    }

    private async Task<string?> ResolvePartitionKeyAsync(
        WebhookEnvelope envelope,
        string? partitionKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(partitionKey))
        {
            return partitionKey;
        }

        if (partitionResolver == null)
        {
            return null;
        }

        return await partitionResolver.ResolvePartitionKeyAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private IInbox GetInbox(string? partitionKey)
    {
        if (string.IsNullOrWhiteSpace(partitionKey) || inboxRouter == null)
        {
            return inbox;
        }

        return inboxRouter.GetInbox(partitionKey);
    }

    private DateTimeOffset GetNeverDueTimeUtc()
    {
        var now = timeProvider.GetUtcNow();
        return now.AddYears(100);
    }
}
