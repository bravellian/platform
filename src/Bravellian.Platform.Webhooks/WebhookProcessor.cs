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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Bravellian.Platform;

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Default processor for webhook inbox messages.
/// </summary>
public sealed class WebhookProcessor : IWebhookProcessor
{
    private readonly IInboxWorkStore workStore;
    private readonly IWebhookProviderRegistry providerRegistry;
    private readonly WebhookProcessorOptions options;
    private readonly WebhookOptions webhookOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookProcessor"/> class.
    /// </summary>
    /// <param name="workStore">Inbox work store.</param>
    /// <param name="providerRegistry">Provider registry.</param>
    /// <param name="options">Processor options.</param>
    /// <param name="webhookOptions">Webhook options for callbacks.</param>
    public WebhookProcessor(
        IInboxWorkStore workStore,
        IWebhookProviderRegistry providerRegistry,
        WebhookProcessorOptions? options = null,
        WebhookOptions? webhookOptions = null)
    {
        this.workStore = workStore ?? throw new ArgumentNullException(nameof(workStore));
        this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        this.options = options ?? new WebhookProcessorOptions();
        this.webhookOptions = webhookOptions ?? new WebhookOptions();
    }

    /// <inheritdoc />
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var ownerToken = OwnerToken.GenerateNew();
        var claimedIds = await workStore.ClaimAsync(ownerToken, options.LeaseSeconds, options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (claimedIds.Count == 0)
        {
            return 0;
        }

        var toAck = new List<string>();
        var toRetry = new Dictionary<string, string>(StringComparer.Ordinal);
        var retryContexts = new Dictionary<string, WebhookEventContext?>(StringComparer.Ordinal);

        foreach (var messageId in claimedIds)
        {
            var outcome = await ProcessSingleAsync(ownerToken, messageId, cancellationToken).ConfigureAwait(false);
            switch (outcome.Disposition)
            {
                case ProcessDisposition.Ack:
                    toAck.Add(messageId);
                    NotifyProcessed(outcome, outcome.Status ?? WebhookEventStatus.Completed);
                    break;
                case ProcessDisposition.Retry:
                    toRetry[messageId] = outcome.ErrorMessage ?? "Webhook processing failed.";
                    retryContexts[messageId] = outcome.Context;
                    break;
                case ProcessDisposition.None:
                    NotifyProcessed(outcome, outcome.Status ?? WebhookEventStatus.Poisoned);
                    break;
            }
        }

        if (toAck.Count > 0)
        {
            await workStore.AckAsync(ownerToken, toAck, cancellationToken).ConfigureAwait(false);
        }

        if (toRetry.Count > 0)
        {
            await HandleFailuresAsync(ownerToken, toRetry, retryContexts, cancellationToken).ConfigureAwait(false);
        }

        return claimedIds.Count;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Webhook processing should retry on unexpected failures.")]
    private async Task<ProcessOutcome> ProcessSingleAsync(
        OwnerToken ownerToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await workStore.GetAsync(messageId, cancellationToken).ConfigureAwait(false);
            var record = DeserializeRecord(message.Payload);
            if (record == null)
            {
                return ProcessOutcome.Retry(messageId, null, message.Attempt, "Webhook payload could not be parsed.");
            }

            var context = BuildContext(record);
            if (record.Status == WebhookEventStatus.Rejected)
            {
                return ProcessOutcome.Ack(messageId, context, message.Attempt, WebhookEventStatus.Rejected);
            }

            var provider = providerRegistry.Get(record.Provider);
            if (provider == null)
            {
                return ProcessOutcome.Retry(messageId, context, message.Attempt, $"Provider '{record.Provider}' is not registered.");
            }

            var processingRecord = record with
            {
                Status = WebhookEventStatus.Processing,
                AttemptCount = message.Attempt,
                NextAttemptUtc = message.DueTimeUtc,
            };

            var eventType = processingRecord.EventType ?? string.Empty;
            var handlers = provider.Handlers
                .Where(handler => !string.IsNullOrWhiteSpace(eventType) && handler.CanHandle(eventType))
                .ToList();

            if (handlers.Count == 0)
            {
                return await HandleMissingHandlerAsync(ownerToken, messageId, eventType, context, message.Attempt, cancellationToken)
                    .ConfigureAwait(false);
            }

            var processingContext = BuildContext(processingRecord);
            foreach (var handler in handlers)
            {
                await handler.HandleAsync(processingContext, cancellationToken).ConfigureAwait(false);
            }

            return ProcessOutcome.Ack(messageId, processingContext, message.Attempt, WebhookEventStatus.Completed);
        }
        catch (Exception ex)
        {
            return ProcessOutcome.Retry(messageId, null, 0, ex.Message);
        }
    }

    private async Task<ProcessOutcome> HandleMissingHandlerAsync(
        OwnerToken ownerToken,
        string messageId,
        string eventType,
        WebhookEventContext context,
        int attempt,
        CancellationToken cancellationToken)
    {
        switch (options.MissingHandlerBehavior)
        {
            case WebhookMissingHandlerBehavior.Retry:
                return ProcessOutcome.Retry(
                    messageId,
                    context,
                    attempt,
                    string.IsNullOrWhiteSpace(eventType)
                        ? "No handler registered for empty event type."
                        : $"No handler registered for event type '{eventType}'.");
            case WebhookMissingHandlerBehavior.Poison:
                await workStore.FailAsync(ownerToken, new[] { messageId }, "No handler registered.", cancellationToken)
                    .ConfigureAwait(false);
                return ProcessOutcome.None(messageId, context, attempt, WebhookEventStatus.Poisoned);
            case WebhookMissingHandlerBehavior.Complete:
            default:
                return ProcessOutcome.Ack(messageId, context, attempt, WebhookEventStatus.Completed);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Failure handling should continue when individual message inspection fails.")]
    private async Task HandleFailuresAsync(
        OwnerToken ownerToken,
        IDictionary<string, string> failedMessages,
        IDictionary<string, WebhookEventContext?> contexts,
        CancellationToken cancellationToken)
    {
        var toFail = new List<string>();
        var toAbandon = new List<(string MessageId, TimeSpan Delay, string Error)>();

        foreach (var (messageId, error) in failedMessages)
        {
            try
            {
                var message = await workStore.GetAsync(messageId, cancellationToken).ConfigureAwait(false);
                if (message.Attempt >= options.MaxAttempts)
                {
                    toFail.Add(messageId);
                    NotifyProcessed(messageId, message.Attempt, WebhookEventStatus.Poisoned, error, contexts, message.Payload);
                }
                else
                {
                    var delay = ComputeBackoff(message.Attempt + 1);
                    toAbandon.Add((messageId, delay, error));
                    NotifyProcessed(messageId, message.Attempt, WebhookEventStatus.FailedRetryable, error, contexts, message.Payload);
                }
            }
            catch
            {
                toAbandon.Add((messageId, TimeSpan.Zero, error));
            }
        }

        foreach (var (messageId, delay, error) in toAbandon)
        {
            await workStore.AbandonAsync(ownerToken, new[] { messageId }, error, delay, cancellationToken).ConfigureAwait(false);
        }

        if (toFail.Count > 0)
        {
            await workStore.FailAsync(ownerToken, toFail, "Maximum retry attempts exceeded", cancellationToken).ConfigureAwait(false);
        }
    }

    private void NotifyProcessed(ProcessOutcome outcome, WebhookEventStatus status)
    {
        if (outcome.Context == null)
        {
            return;
        }

        webhookOptions.OnProcessed?.Invoke(
            new ProcessingResult(status, outcome.AttemptCount, outcome.ErrorMessage),
            outcome.Context);
    }

    private void NotifyProcessed(
        string messageId,
        int attempt,
        WebhookEventStatus status,
        string? errorMessage,
        IDictionary<string, WebhookEventContext?> contexts,
        string payload)
    {
        if (!contexts.TryGetValue(messageId, out var context) || context == null)
        {
            var record = DeserializeRecord(payload);
            context = record == null ? null : BuildContext(record);
        }

        if (context == null)
        {
            return;
        }

        webhookOptions.OnProcessed?.Invoke(
            new ProcessingResult(status, attempt, errorMessage),
            context);
    }

    private static WebhookEventContext BuildContext(WebhookEventRecord record)
    {
        var headers = DeserializeHeaders(record.HeadersJson);
        return new WebhookEventContext(
            record.Provider,
            record.DedupeKey,
            record.ProviderEventId,
            record.EventType,
            record.PartitionKey,
            record.ReceivedAtUtc,
            headers,
            record.BodyBytes,
            record.ContentType);
    }

    private static WebhookEventRecord? DeserializeRecord(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<WebhookEventRecord>(payload);
    }

    private static Dictionary<string, string> DeserializeHeaders(string headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
        if (parsed == null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        if (attempt <= 0)
        {
            attempt = 1;
        }

        var baseMs = options.BaseBackoff.TotalMilliseconds;
        var delayMs = baseMs * Math.Pow(2, Math.Min(10, attempt - 1));
        var delay = TimeSpan.FromMilliseconds(delayMs);
        if (delay > options.MaxBackoff)
        {
            delay = options.MaxBackoff;
        }

        return delay;
    }

    private enum ProcessDisposition
    {
        Ack,
        Retry,
        None,
    }

    private readonly struct ProcessOutcome
    {
        private ProcessOutcome(
            string messageId,
            WebhookEventContext? context,
            int attemptCount,
            ProcessDisposition disposition,
            WebhookEventStatus? status,
            string? errorMessage)
        {
            MessageId = messageId;
            Context = context;
            AttemptCount = attemptCount;
            Disposition = disposition;
            Status = status;
            ErrorMessage = errorMessage;
        }

        public string MessageId { get; }

        public WebhookEventContext? Context { get; }

        public int AttemptCount { get; }

        public ProcessDisposition Disposition { get; }

        public WebhookEventStatus? Status { get; }

        public string? ErrorMessage { get; }

        public static ProcessOutcome Ack(string messageId, WebhookEventContext context, int attempt, WebhookEventStatus status)
        {
            return new ProcessOutcome(messageId, context, attempt, ProcessDisposition.Ack, status, null);
        }

        public static ProcessOutcome Retry(string messageId, WebhookEventContext? context, int attempt, string? errorMessage)
        {
            return new ProcessOutcome(messageId, context, attempt, ProcessDisposition.Retry, null, errorMessage);
        }

        public static ProcessOutcome None(string messageId, WebhookEventContext context, int attempt, WebhookEventStatus status)
        {
            return new ProcessOutcome(messageId, context, attempt, ProcessDisposition.None, status, null);
        }
    }
}
