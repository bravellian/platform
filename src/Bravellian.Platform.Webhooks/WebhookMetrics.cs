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
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Observability;

namespace Bravellian.Platform.Webhooks;

internal static class WebhookMetrics
{
    private static readonly PlatformMeterProvider MeterProvider = new(
        "Bravellian.Platform.Webhooks",
        "1.0.0");
    private static readonly Meter Meter = MeterProvider.Meter;

    private static readonly Counter<long> WebhookReceivedTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.received_total", unit: "items", description: "Total number of webhook requests received.");

    private static readonly Counter<long> WebhookAcceptedTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.accepted_total", unit: "items", description: "Total number of webhook requests accepted.");

    private static readonly Counter<long> WebhookRejectedTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.rejected_total", unit: "items", description: "Total number of webhook requests rejected.");

    private static readonly Counter<long> WebhookDuplicateTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.duplicate_total", unit: "items", description: "Total number of duplicate webhook events detected.");

    private static readonly Counter<long> WebhookClaimedTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.claimed_total", unit: "items", description: "Total number of webhook work items claimed.");

    private static readonly Counter<long> WebhookProcessedTotal =
        Meter.CreateCounter<long>("bravellian.platform.webhook.processed_total", unit: "items", description: "Total number of webhook work items processed.");

    private static readonly Histogram<double> WebhookProcessingDuration =
        Meter.CreateHistogram<double>("bravellian.platform.webhook.processing_duration_ms", unit: "ms", description: "Webhook processing duration.");

    public static void RecordReceived(string provider)
    {
        WebhookReceivedTotal.Add(1, BuildProviderTags(provider));
    }

    public static void RecordAccepted(string provider)
    {
        WebhookAcceptedTotal.Add(1, BuildProviderTags(provider));
    }

    public static void RecordRejected(string provider, string? reason)
    {
        WebhookRejectedTotal.Add(1, BuildProviderTags(provider, reason));
    }

    public static void RecordDuplicate(string provider)
    {
        WebhookDuplicateTotal.Add(1, BuildProviderTags(provider));
    }

    public static void RecordClaimed(int count)
    {
        if (count <= 0)
        {
            return;
        }

        WebhookClaimedTotal.Add(count);
    }

    public static void RecordProcessed(string? provider, WebhookEventStatus status, TimeSpan duration)
    {
        var tags = BuildStatusTags(provider, status);
        WebhookProcessedTotal.Add(1, tags);
        WebhookProcessingDuration.Record(duration.TotalMilliseconds, tags);
    }

    private static KeyValuePair<string, object?>[] BuildProviderTags(string provider, string? reason = null)
    {
        return new[]
        {
            new KeyValuePair<string, object?>(PlatformTagKeys.Provider, NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("reason", reason),
        };
    }

    private static KeyValuePair<string, object?>[] BuildStatusTags(string? provider, WebhookEventStatus status)
    {
        return new[]
        {
            new KeyValuePair<string, object?>(PlatformTagKeys.Provider, NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("status", status.ToString()),
        };
    }

    private static string NormalizeProvider(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider) ? "unknown" : provider.Trim();
    }
}
