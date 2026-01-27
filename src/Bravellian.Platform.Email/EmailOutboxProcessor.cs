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
using Bravellian.Platform.Idempotency;

namespace Bravellian.Platform.Email;

/// <summary>
/// Processes outbound email messages stored in the platform outbox.
/// </summary>
public sealed class EmailOutboxProcessor : IEmailOutboxProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IOutboxStore outboxStore;
    private readonly IOutboundEmailSender sender;
    private readonly IIdempotencyStore idempotencyStore;
    private readonly IEmailDeliverySink deliverySink;
    private readonly IEmailSendPolicy policy;
    private readonly TimeProvider timeProvider;
    private readonly EmailOutboxProcessorOptions options;
    private readonly Func<int, TimeSpan> backoffPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutboxProcessor"/> class.
    /// </summary>
    /// <param name="outboxStore">Outbox store.</param>
    /// <param name="sender">Outbound email sender.</param>
    /// <param name="idempotencyStore">Idempotency store.</param>
    /// <param name="deliverySink">Delivery sink.</param>
    /// <param name="policy">Send policy.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="options">Processor options.</param>
    public EmailOutboxProcessor(
        IOutboxStore outboxStore,
        IOutboundEmailSender sender,
        IIdempotencyStore idempotencyStore,
        IEmailDeliverySink deliverySink,
        IEmailSendPolicy? policy = null,
        TimeProvider? timeProvider = null,
        EmailOutboxProcessorOptions? options = null)
    {
        this.outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        this.idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        this.deliverySink = deliverySink ?? throw new ArgumentNullException(nameof(deliverySink));
        this.policy = policy ?? NoOpEmailSendPolicy.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.options = options ?? new EmailOutboxProcessorOptions();
        backoffPolicy = this.options.BackoffPolicy ?? EmailOutboxDefaults.DefaultBackoff;
    }

    /// <summary>
    /// Processes a single batch of outbound email messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of messages processed.</returns>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var messages = await outboxStore.ClaimDueAsync(options.BatchSize, cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return 0;
        }

        var processed = 0;

        foreach (var message in messages)
        {
            if (!string.Equals(message.Topic, options.Topic, StringComparison.OrdinalIgnoreCase))
            {
                await outboxStore.FailAsync(
                    message.Id,
                    $"Unexpected outbox topic '{message.Topic}'.",
                    cancellationToken).ConfigureAwait(false);
                processed++;
                continue;
            }

            if (!await TryBeginIdempotencyAsync(message, cancellationToken).ConfigureAwait(false))
            {
                processed++;
                continue;
            }

            processed++;
            await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }

    private async Task<bool> TryBeginIdempotencyAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = Deserialize(message, out var deserializeError);
        if (payload == null)
        {
            await outboxStore.FailAsync(message.Id, deserializeError ?? "Invalid payload.", cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (!await idempotencyStore.TryBeginAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false))
        {
            var duplicateAttempt = new EmailDeliveryAttempt(
                message.RetryCount + 1,
                timeProvider.GetUtcNow(),
                EmailDeliveryStatus.Suppressed,
                null,
                null,
                "Duplicate message key suppressed.");
            await deliverySink.RecordAttemptAsync(payload, duplicateAttempt, cancellationToken).ConfigureAwait(false);
            await deliverySink.RecordFinalAsync(
                payload,
                EmailDeliveryStatus.Suppressed,
                null,
                null,
                "Duplicate message key suppressed.",
                cancellationToken).ConfigureAwait(false);
            await outboxStore.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = Deserialize(message, out var deserializeError);
        if (payload == null)
        {
            await outboxStore.FailAsync(message.Id, deserializeError ?? "Invalid payload.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var attemptNumber = message.RetryCount + 1;
        var policyDecision = await policy.EvaluateAsync(payload, cancellationToken).ConfigureAwait(false);
        if (policyDecision.Outcome == EmailPolicyOutcome.Delay)
        {
            var delayUntilUtc = policyDecision.DelayUntilUtc ?? timeProvider.GetUtcNow().AddMinutes(1);
            var delay = delayUntilUtc - timeProvider.GetUtcNow();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            var delayAttempt = new EmailDeliveryAttempt(
                attemptNumber,
                timeProvider.GetUtcNow(),
                EmailDeliveryStatus.Queued,
                null,
                null,
                policyDecision.Reason ?? "Send delayed by policy.");
            await deliverySink.RecordAttemptAsync(payload, delayAttempt, cancellationToken).ConfigureAwait(false);
            await outboxStore.RescheduleAsync(message.Id, delay, policyDecision.Reason ?? "Policy delay", cancellationToken)
                .ConfigureAwait(false);
            await idempotencyStore.FailAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (policyDecision.Outcome == EmailPolicyOutcome.Reject)
        {
            var reason = policyDecision.Reason ?? "Send rejected by policy.";
            var rejectionAttempt = new EmailDeliveryAttempt(
                attemptNumber,
                timeProvider.GetUtcNow(),
                EmailDeliveryStatus.FailedPermanent,
                null,
                null,
                reason);
            await deliverySink.RecordAttemptAsync(payload, rejectionAttempt, cancellationToken).ConfigureAwait(false);
            await outboxStore.FailAsync(message.Id, reason, cancellationToken).ConfigureAwait(false);
            await idempotencyStore.CompleteAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false);
            await deliverySink.RecordFinalAsync(
                payload,
                EmailDeliveryStatus.FailedPermanent,
                null,
                null,
                reason,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var sendResult = await sender.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        var sendAttempt = new EmailDeliveryAttempt(
            attemptNumber,
            timeProvider.GetUtcNow(),
            sendResult.Status,
            sendResult.ProviderMessageId,
            sendResult.ErrorCode,
            sendResult.ErrorMessage);
        await deliverySink.RecordAttemptAsync(payload, sendAttempt, cancellationToken).ConfigureAwait(false);

        if (sendResult.Status == EmailDeliveryStatus.Sent
            || sendResult.Status == EmailDeliveryStatus.Bounced
            || sendResult.Status == EmailDeliveryStatus.Suppressed)
        {
            await outboxStore.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            await idempotencyStore.CompleteAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false);
            await deliverySink.RecordFinalAsync(
                payload,
                sendResult.Status,
                sendResult.ProviderMessageId,
                sendResult.ErrorCode,
                sendResult.ErrorMessage,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (sendResult.Status == EmailDeliveryStatus.FailedTransient && attemptNumber < options.MaxAttempts)
        {
            var delay = backoffPolicy(attemptNumber);
            await outboxStore.RescheduleAsync(message.Id, delay, sendResult.ErrorMessage ?? "Transient failure", cancellationToken)
                .ConfigureAwait(false);
            await idempotencyStore.FailAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        await outboxStore.FailAsync(message.Id, sendResult.ErrorMessage ?? "Permanent failure", cancellationToken).ConfigureAwait(false);
        await idempotencyStore.CompleteAsync(payload.MessageKey, cancellationToken).ConfigureAwait(false);
        await deliverySink.RecordFinalAsync(
            payload,
            EmailDeliveryStatus.FailedPermanent,
            sendResult.ProviderMessageId,
            sendResult.ErrorCode,
            sendResult.ErrorMessage,
            cancellationToken).ConfigureAwait(false);
    }

    private static OutboundEmailMessage? Deserialize(OutboxMessage message, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            error = "Outbox payload is empty.";
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OutboundEmailMessage>(message.Payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }
}


