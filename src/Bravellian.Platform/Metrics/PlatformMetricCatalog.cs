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

namespace Bravellian.Platform.Metrics;

using System.Collections.Generic;

/// <summary>
/// Catalog of predefined platform metrics.
/// </summary>
public static class PlatformMetricCatalog
{
    /// <summary>
    /// Gets all platform metrics.
    /// </summary>
    public static IReadOnlyList<MetricRegistration> All => new[]
    {
        // Outbox metrics
        new MetricRegistration(
            "outbox.published.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of messages published to the outbox",
            new[] { "topic", "result" }),

        new MetricRegistration(
            "outbox.pending.count",
            MetricUnit.Count,
            MetricAggregationKind.Gauge,
            "Number of pending messages in the outbox",
            new[] { "topic" }),

        new MetricRegistration(
            "outbox.oldest_age.seconds",
            MetricUnit.Seconds,
            MetricAggregationKind.Gauge,
            "Age in seconds of the oldest pending outbox message",
            new[] { "topic" }),

        new MetricRegistration(
            "outbox.publish_latency.ms",
            MetricUnit.Milliseconds,
            MetricAggregationKind.Histogram,
            "Time taken to publish a message from the outbox",
            new[] { "topic" }),

        // Inbox metrics
        new MetricRegistration(
            "inbox.processed.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of messages processed from the inbox",
            new[] { "topic", "result" }),

        new MetricRegistration(
            "inbox.retry.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of inbox message retries",
            new[] { "topic", "reason" }),

        new MetricRegistration(
            "inbox.failed.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of inbox messages that failed permanently",
            new[] { "topic", "reason" }),

        new MetricRegistration(
            "inbox.processing_latency.ms",
            MetricUnit.Milliseconds,
            MetricAggregationKind.Histogram,
            "Time taken to process an inbox message",
            new[] { "topic" }),

        // DLQ metrics
        new MetricRegistration(
            "dlq.depth",
            MetricUnit.Count,
            MetricAggregationKind.Gauge,
            "Number of messages in the dead letter queue",
            new[] { "queue" }),

        new MetricRegistration(
            "dlq.oldest_age.seconds",
            MetricUnit.Seconds,
            MetricAggregationKind.Gauge,
            "Age in seconds of the oldest message in the DLQ",
            new[] { "queue" }),

        // Recon metrics
        new MetricRegistration(
            "recon.gap_aggregates.count",
            MetricUnit.Count,
            MetricAggregationKind.Gauge,
            "Number of reconciliation gap aggregates detected",
            new[] { "kind" }),

        // Scheduler metrics
        new MetricRegistration(
            "scheduler.job.executed.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of scheduled jobs executed",
            new[] { "job_name", "result" }),

        new MetricRegistration(
            "scheduler.job.latency.ms",
            MetricUnit.Milliseconds,
            MetricAggregationKind.Histogram,
            "Time taken to execute a scheduled job",
            new[] { "job_name" }),

        // Lease metrics
        new MetricRegistration(
            "lease.acquired.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of leases acquired",
            new[] { "resource", "result" }),

        new MetricRegistration(
            "lease.active.count",
            MetricUnit.Count,
            MetricAggregationKind.Gauge,
            "Number of currently active leases",
            new[] { "resource" }),
    };
}
