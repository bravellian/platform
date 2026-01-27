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

using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Observability;

namespace Bravellian.Platform.Email;

/// <summary>
/// Metrics for email delivery.
/// </summary>
public static class EmailMetrics
{
    private static readonly PlatformMeterProvider MeterProvider = new(
        "Bravellian.Platform.Email",
        "1.0.0");
    private static readonly Meter Meter = MeterProvider.Meter;

    private static readonly Counter<long> EmailQueuedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.queued_total", unit: "items", description: "Total number of emails queued.");

    private static readonly Counter<long> EmailAttemptedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.attempted_total", unit: "items", description: "Total number of email send attempts.");

    private static readonly Counter<long> EmailSucceededTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.succeeded_total", unit: "items", description: "Total number of emails sent successfully.");

    private static readonly Counter<long> EmailFailedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.failed_total", unit: "items", description: "Total number of emails that failed permanently.");

    private static readonly Counter<long> EmailSuppressedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.suppressed_total", unit: "items", description: "Total number of emails suppressed.");

    private static readonly Counter<long> EmailBouncedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.bounced_total", unit: "items", description: "Total number of emails that bounced.");

    private static readonly Counter<long> WebhookReceivedTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.webhook_received_total", unit: "items", description: "Total number of email webhooks received.");

    private static readonly Counter<long> EmailDeliveryEventTotal =
        Meter.CreateCounter<long>("bravellian.platform.email.delivery_event_total", unit: "items", description: "Total number of email delivery events recorded.");

    private static readonly Histogram<long> EmailBodyBytes =
        Meter.CreateHistogram<long>("bravellian.platform.email.body_bytes", unit: "bytes", description: "Total email body size.");

    private static readonly Histogram<long> EmailAttachmentBytes =
        Meter.CreateHistogram<long>("bravellian.platform.email.attachment_bytes", unit: "bytes", description: "Total email attachment size (raw bytes).");

    private static readonly Histogram<long> EmailTotalBytes =
        Meter.CreateHistogram<long>("bravellian.platform.email.total_bytes", unit: "bytes", description: "Total email size (body + base64 attachments).");

    /// <summary>
    /// Records a queued email.
    /// </summary>
    public static void RecordQueued(OutboundEmailMessage message, string? provider)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tags = BuildTags(message, provider);
        EmailQueuedTotal.Add(1, tags);
        RecordSizes(message, tags);
    }

    /// <summary>
    /// Records an email send attempt.
    /// </summary>
    public static void RecordAttempted(OutboundEmailMessage message, string? provider)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tags = BuildTags(message, provider);
        EmailAttemptedTotal.Add(1, tags);
        RecordSizes(message, tags);
    }

    /// <summary>
    /// Records an email send result.
    /// </summary>
    public static void RecordResult(OutboundEmailMessage message, EmailDeliveryStatus status, string? provider)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tags = BuildTags(message, provider, status.ToString());
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                EmailSucceededTotal.Add(1, tags);
                break;
            case EmailDeliveryStatus.Bounced:
                EmailBouncedTotal.Add(1, tags);
                break;
            case EmailDeliveryStatus.Suppressed:
                EmailSuppressedTotal.Add(1, tags);
                break;
            case EmailDeliveryStatus.FailedPermanent:
                EmailFailedTotal.Add(1, tags);
                break;
            default:
                break;
        }

        RecordSizes(message, tags);
    }

    /// <summary>
    /// Records a webhook receipt.
    /// </summary>
    public static void RecordWebhookReceived(string provider, string? eventType)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>(PlatformTagKeys.Provider, NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("eventType", eventType)
        };

        WebhookReceivedTotal.Add(1, tags);
    }

    /// <summary>
    /// Records a delivery event captured by a sink.
    /// </summary>
    public static void RecordDeliveryEvent(string eventType, EmailDeliveryStatus status, string? provider)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>(PlatformTagKeys.Provider, NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("eventType", eventType),
            new KeyValuePair<string, object?>("status", status.ToString()),
        };

        EmailDeliveryEventTotal.Add(1, tags);
    }

    internal static EmailSizeInfo GetSizeInfo(OutboundEmailMessage message)
    {
        var bodyBytes = GetByteCount(message.TextBody) + GetByteCount(message.HtmlBody);
        long attachmentBytes = 0;
        long attachmentEncodedBytes = 0;

        if (message.Attachments.Count > 0)
        {
            foreach (var attachment in message.Attachments)
            {
                attachmentBytes += attachment.ContentBytes.Length;
                attachmentEncodedBytes += GetBase64EncodedSize(attachment.ContentBytes.Length);
            }
        }

        var totalBytes = bodyBytes + attachmentEncodedBytes;
        return new EmailSizeInfo(bodyBytes, attachmentBytes, totalBytes);
    }

    private static void RecordSizes(OutboundEmailMessage message, KeyValuePair<string, object?>[] tags)
    {
        var info = GetSizeInfo(message);
        EmailBodyBytes.Record(info.BodyBytes, tags);
        EmailAttachmentBytes.Record(info.AttachmentBytes, tags);
        EmailTotalBytes.Record(info.TotalBytes, tags);
    }

    private static KeyValuePair<string, object?>[] BuildTags(
        OutboundEmailMessage message,
        string? provider,
        string? status = null)
    {
        return new[]
        {
            new KeyValuePair<string, object?>(PlatformTagKeys.Provider, NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("hasAttachments", message.Attachments.Count > 0),
            new KeyValuePair<string, object?>("attachmentCount", message.Attachments.Count),
        };
    }

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "unknown" : provider.Trim();
    }

    private static long GetByteCount(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : System.Text.Encoding.UTF8.GetByteCount(value);
    }

    private static long GetBase64EncodedSize(long rawBytes)
    {
        if (rawBytes <= 0)
        {
            return 0;
        }

        return ((rawBytes + 2) / 3) * 4;
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct EmailSizeInfo(long BodyBytes, long AttachmentBytes, long TotalBytes);
}
