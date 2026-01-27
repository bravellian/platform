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
using Bravellian.Platform.ExactlyOnce;
using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Observability;

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
    private static readonly IExactlyOnceKeyResolver<OutboundEmailMessage> MessageKeyResolver = new EmailMessageKeyResolver();

    private readonly IOutboxStore outboxStore;
    private readonly IOutboundEmailSender sender;
    private readonly IIdempotencyStore idempotencyStore;
    private readonly IEmailDeliverySink deliverySink;
    private readonly IOutboundEmailProbe? probe;
    private readonly IPlatformEventEmitter? eventEmitter;
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
    /// <param name="probe">Optional outbound probe.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    /// <param name="policy">Send policy.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="options">Processor options.</param>
    public EmailOutboxProcessor(
        IOutboxStore outboxStore,
        IOutboundEmailSender sender,
        IIdempotencyStore idempotencyStore,
        IEmailDeliverySink deliverySink,
        IOutboundEmailProbe? probe = null,
        IPlatformEventEmitter? eventEmitter = null,
        IEmailSendPolicy? policy = null,
        TimeProvider? timeProvider = null,
        EmailOutboxProcessorOptions? options = null)
    {
        this.outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        this.idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        this.deliverySink = deliverySink ?? throw new ArgumentNullException(nameof(deliverySink));
        this.probe = probe;
        this.eventEmitter = eventEmitter;
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

            var payload = Deserialize(message, out var deserializeError);
            if (payload == null)
            {
                await outboxStore.FailAsync(message.Id, deserializeError ?? "Invalid payload.", cancellationToken)
                    .ConfigureAwait(false);
                processed++;
                continue;
            }

            processed++;
            await ProcessMessageAsync(message, payload, cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }

    private async Task ProcessMessageAsync(OutboxMessage message, OutboundEmailMessage payload, CancellationToken cancellationToken)
    {
        var attemptNumber = message.RetryCount + 1;
        var retryDelay = (TimeSpan?)null;
        var retryReason = (string?)null;

        var executor = new ExactlyOnceExecutor<OutboundEmailMessage>(idempotencyStore, MessageKeyResolver);
        var result = await executor.ExecuteAsync(
            payload,
            async (email, ct) =>
            {
                var policyDecision = await policy.EvaluateAsync(email, ct).ConfigureAwait(false);
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
                    await deliverySink.RecordAttemptAsync(email, delayAttempt, ct).ConfigureAwait(false);

                    retryDelay = delay;
                    retryReason = policyDecision.Reason ?? "Policy delay";
                    return ExactlyOnceExecutionResult.TransientFailure(errorMessage: retryReason, allowProbe: false);
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
                    await deliverySink.RecordAttemptAsync(email, rejectionAttempt, ct).ConfigureAwait(false);
                    await deliverySink.RecordFinalAsync(
                        email,
                        EmailDeliveryStatus.FailedPermanent,
                        null,
                        null,
                        reason,
                        ct).ConfigureAwait(false);
                    EmailMetrics.RecordResult(email, EmailDeliveryStatus.FailedPermanent, provider: null);
                    await EmailAuditEvents.EmitFinalAsync(
                        eventEmitter,
                        email,
                        provider: null,
                        EmailDeliveryStatus.FailedPermanent,
                        errorCode: null,
                        errorMessage: reason,
                        ct).ConfigureAwait(false);
                    return ExactlyOnceExecutionResult.PermanentFailure(errorMessage: reason, allowProbe: false);
                }

                var sendResult = await sender.SendAsync(email, ct).ConfigureAwait(false);
                var sendAttempt = new EmailDeliveryAttempt(
                    attemptNumber,
                    timeProvider.GetUtcNow(),
                    sendResult.Status,
                    sendResult.ProviderMessageId,
                    sendResult.ErrorCode,
                    sendResult.ErrorMessage);
                await deliverySink.RecordAttemptAsync(email, sendAttempt, ct).ConfigureAwait(false);
                EmailMetrics.RecordAttempted(email, provider: null);
                await EmailAuditEvents.EmitAttemptedAsync(
                    eventEmitter,
                    email,
                    provider: null,
                    attemptNumber,
                    sendResult.Status,
                    ct).ConfigureAwait(false);

                if (sendResult.Status == EmailDeliveryStatus.FailedTransient
                    || sendResult.Status == EmailDeliveryStatus.FailedPermanent)
                {
                    if (probe != null && !IsValidationFailure(sendResult))
                    {
                        var probeResult = await probe.ProbeAsync(email, ct).ConfigureAwait(false);
                        if (probeResult.Outcome == EmailProbeOutcome.Confirmed)
                        {
                            var confirmedStatus = probeResult.Status ?? EmailDeliveryStatus.Sent;
                            await deliverySink.RecordFinalAsync(
                                email,
                                confirmedStatus,
                                probeResult.ProviderMessageId,
                                probeResult.ErrorCode,
                                probeResult.ErrorMessage,
                                ct).ConfigureAwait(false);
                            EmailMetrics.RecordResult(email, confirmedStatus, provider: null);
                            await EmailAuditEvents.EmitFinalAsync(
                                eventEmitter,
                                email,
                                provider: null,
                                confirmedStatus,
                                probeResult.ErrorCode,
                                probeResult.ErrorMessage,
                                ct).ConfigureAwait(false);
                            return ExactlyOnceExecutionResult.Success();
                        }
                    }
                }

                if (sendResult.Status == EmailDeliveryStatus.Sent
                    || sendResult.Status == EmailDeliveryStatus.Bounced
                    || sendResult.Status == EmailDeliveryStatus.Suppressed)
                {
                    await deliverySink.RecordFinalAsync(
                        email,
                        sendResult.Status,
                        sendResult.ProviderMessageId,
                        sendResult.ErrorCode,
                        sendResult.ErrorMessage,
                        ct).ConfigureAwait(false);
                    EmailMetrics.RecordResult(email, sendResult.Status, provider: null);
                    await EmailAuditEvents.EmitFinalAsync(
                        eventEmitter,
                        email,
                        provider: null,
                        sendResult.Status,
                        sendResult.ErrorCode,
                        sendResult.ErrorMessage,
                        ct).ConfigureAwait(false);
                    return ExactlyOnceExecutionResult.Success();
                }

                if (sendResult.Status == EmailDeliveryStatus.FailedTransient && attemptNumber < options.MaxAttempts)
                {
                    retryDelay = backoffPolicy(attemptNumber);
                    retryReason = sendResult.ErrorMessage ?? "Transient failure";
                    return ExactlyOnceExecutionResult.TransientFailure(
                        sendResult.ErrorCode,
                        retryReason,
                        allowProbe: false);
                }

                var permanentReason = sendResult.ErrorMessage ?? "Permanent failure";
                await deliverySink.RecordFinalAsync(
                    email,
                    EmailDeliveryStatus.FailedPermanent,
                    sendResult.ProviderMessageId,
                    sendResult.ErrorCode,
                    sendResult.ErrorMessage,
                    ct).ConfigureAwait(false);
                EmailMetrics.RecordResult(email, EmailDeliveryStatus.FailedPermanent, provider: null);
                await EmailAuditEvents.EmitFinalAsync(
                    eventEmitter,
                    email,
                    provider: null,
                    EmailDeliveryStatus.FailedPermanent,
                    sendResult.ErrorCode,
                    sendResult.ErrorMessage,
                    ct).ConfigureAwait(false);
                return ExactlyOnceExecutionResult.PermanentFailure(sendResult.ErrorCode, permanentReason, allowProbe: false);
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Outcome == ExactlyOnceOutcome.Suppressed)
        {
            var duplicateAttempt = new EmailDeliveryAttempt(
                attemptNumber,
                timeProvider.GetUtcNow(),
                EmailDeliveryStatus.Suppressed,
                null,
                null,
                result.ErrorMessage ?? ExactlyOnceDefaults.SuppressedReason);
            await deliverySink.RecordAttemptAsync(payload, duplicateAttempt, cancellationToken).ConfigureAwait(false);
            await deliverySink.RecordFinalAsync(
                payload,
                EmailDeliveryStatus.Suppressed,
                null,
                null,
                result.ErrorMessage ?? ExactlyOnceDefaults.SuppressedReason,
                cancellationToken).ConfigureAwait(false);
            EmailMetrics.RecordResult(payload, EmailDeliveryStatus.Suppressed, provider: null);
            await EmailAuditEvents.EmitFinalAsync(
                eventEmitter,
                payload,
                provider: null,
                EmailDeliveryStatus.Suppressed,
                errorCode: null,
                errorMessage: result.ErrorMessage ?? ExactlyOnceDefaults.SuppressedReason,
                cancellationToken).ConfigureAwait(false);
            await outboxStore.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.Outcome == ExactlyOnceOutcome.Completed)
        {
            await outboxStore.MarkDispatchedAsync(message.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.Outcome == ExactlyOnceOutcome.Retry)
        {
            var delay = retryDelay ?? backoffPolicy(attemptNumber);
            var reason = retryReason ?? result.ErrorMessage ?? ExactlyOnceDefaults.TransientFailureReason;
            await outboxStore.RescheduleAsync(message.Id, delay, reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        var finalReason = result.ErrorMessage ?? ExactlyOnceDefaults.PermanentFailureReason;
        await outboxStore.FailAsync(message.Id, finalReason, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValidationFailure(EmailSendResult sendResult)
    {
        return string.Equals(sendResult.ErrorCode, "validation", StringComparison.OrdinalIgnoreCase);
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

    private sealed class EmailMessageKeyResolver : IExactlyOnceKeyResolver<OutboundEmailMessage>
    {
        public string GetKey(OutboundEmailMessage item)
        {
            return item.MessageKey;
        }
    }
}


