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
using Bravellian.Platform.Audit;
using Bravellian.Platform.Observability;

namespace Bravellian.Platform.Email;

public static class EmailAuditEvents
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task EmitQueuedAsync(
        IPlatformEventEmitter? emitter,
        OutboundEmailMessage message,
        string? provider,
        CancellationToken cancellationToken)
    {
        return EmitAsync(
            emitter,
            PlatformEventNames.EmailQueued,
            "Email queued",
            EventOutcome.Info,
            message,
            provider,
            status: "Queued",
            errorCode: null,
            errorMessage: null,
            cancellationToken);
    }

    public static Task EmitAttemptedAsync(
        IPlatformEventEmitter? emitter,
        OutboundEmailMessage message,
        string? provider,
        int attempt,
        EmailDeliveryStatus status,
        CancellationToken cancellationToken)
    {
        var display = $"Email attempt {attempt} ({status})";
        return EmitAsync(
            emitter,
            PlatformEventNames.EmailAttempted,
            display,
            EventOutcome.Info,
            message,
            provider,
            status.ToString(),
            errorCode: null,
            errorMessage: null,
            cancellationToken,
            attempt);
    }

    public static Task EmitFinalAsync(
        IPlatformEventEmitter? emitter,
        OutboundEmailMessage message,
        string? provider,
        EmailDeliveryStatus status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var (name, outcome, display) = status switch
        {
            EmailDeliveryStatus.Sent => (PlatformEventNames.EmailSent, EventOutcome.Success, "Email sent"),
            EmailDeliveryStatus.Bounced => (PlatformEventNames.EmailBounced, EventOutcome.Warning, "Email bounced"),
            EmailDeliveryStatus.Suppressed => (PlatformEventNames.EmailSuppressed, EventOutcome.Warning, "Email suppressed"),
            EmailDeliveryStatus.FailedPermanent => (PlatformEventNames.EmailFailed, EventOutcome.Failure, "Email failed"),
            _ => (PlatformEventNames.EmailFailed, EventOutcome.Failure, $"Email failed ({status})")
        };

        return EmitAsync(
            emitter,
            name,
            display,
            outcome,
            message,
            provider,
            status.ToString(),
            errorCode,
            errorMessage,
            cancellationToken);
    }

    public static Task EmitWebhookReceivedAsync(
        IPlatformEventEmitter? emitter,
        string provider,
        string? eventType,
        string? messageKey,
        string? providerEventId,
        CancellationToken cancellationToken)
    {
        if (emitter is null)
        {
            return Task.CompletedTask;
        }

        var anchorId = messageKey ?? providerEventId ?? "unknown";
        var data = new Dictionary<string, object?>
        {
            [PlatformTagKeys.Provider] = NormalizeProvider(provider),
            [PlatformTagKeys.MessageKey] = messageKey,
            [PlatformTagKeys.WebhookEventId] = providerEventId,
            ["eventType"] = eventType,
        };

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            DateTimeOffset.UtcNow,
            PlatformEventNames.WebhookReceived,
            "Webhook received",
            EventOutcome.Success,
            new[] { new EventAnchor("Email", anchorId, "Subject") },
            JsonSerializer.Serialize(data, SerializerOptions));

        return emitter.EmitAuditEventAsync(auditEvent, cancellationToken);
    }

    private static Task EmitAsync(
        IPlatformEventEmitter? emitter,
        string eventName,
        string displayMessage,
        EventOutcome outcome,
        OutboundEmailMessage message,
        string? provider,
        string? status,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken,
        int? attempt = null)
    {
        if (emitter is null)
        {
            return Task.CompletedTask;
        }

        var size = EmailMetrics.GetSizeInfo(message);
        var data = new Dictionary<string, object?>
        {
            [PlatformTagKeys.MessageKey] = message.MessageKey,
            [PlatformTagKeys.Provider] = NormalizeProvider(provider),
            ["status"] = status,
            ["attempt"] = attempt,
            ["subject"] = message.Subject,
            ["hasAttachments"] = message.Attachments.Count > 0,
            ["attachmentCount"] = message.Attachments.Count,
            ["bodyBytes"] = size.BodyBytes,
            ["attachmentBytes"] = size.AttachmentBytes,
            ["totalBytes"] = size.TotalBytes,
            ["errorCode"] = errorCode,
            ["errorMessage"] = errorMessage,
        };

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            DateTimeOffset.UtcNow,
            eventName,
            displayMessage,
            outcome,
            new[] { new EventAnchor("Email", message.MessageKey, "Subject") },
            JsonSerializer.Serialize(data, SerializerOptions));

        return emitter.EmitAuditEventAsync(auditEvent, cancellationToken);
    }

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "unknown" : provider.Trim();
    }
}
