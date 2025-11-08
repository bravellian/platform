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

namespace Bravellian.Platform.Observability;

using System.Diagnostics.Metrics;

/// <summary>
/// Contains all platform observability metrics.
/// </summary>
internal static class PlatformMeters
{
    internal static readonly Meter Meter = new("Bravellian.Platform", "1.0.0");

    // Watchdog & Heartbeat
    internal static readonly Counter<long> WatchdogHeartbeatTotal =
        Meter.CreateCounter<long>("bravellian.platform.watchdog.heartbeat_total", unit: "items", description: "Total number of watchdog heartbeats.");

    internal static readonly Counter<long> WatchdogAlertsTotal =
        Meter.CreateCounter<long>("bravellian.platform.watchdog.alerts_total", unit: "items", description: "Total number of watchdog alerts raised.");

    // Scheduler
    internal static readonly Counter<long> SchedulerJobsDueTotal =
        Meter.CreateCounter<long>("bravellian.platform.scheduler.jobs_due_total", unit: "items", description: "Total number of jobs that became due.");

    internal static readonly Counter<long> SchedulerJobsExecutedTotal =
        Meter.CreateCounter<long>("bravellian.platform.scheduler.jobs_executed_total", unit: "items", description: "Total number of jobs executed.");

    internal static readonly Histogram<double> SchedulerJobDelay =
        Meter.CreateHistogram<double>("bravellian.platform.scheduler.job_delay", unit: "s", description: "Job delay: start time - due time.");

    internal static readonly Histogram<double> SchedulerJobRuntime =
        Meter.CreateHistogram<double>("bravellian.platform.scheduler.job_runtime", unit: "s", description: "Job execution duration.");

    // Outbox
    internal static readonly Counter<long> OutboxEnqueuedTotal =
        Meter.CreateCounter<long>("bravellian.platform.outbox.enqueued_total", unit: "items", description: "Total number of messages enqueued to outbox.");

    internal static readonly Counter<long> OutboxDequeuedTotal =
        Meter.CreateCounter<long>("bravellian.platform.outbox.dequeued_total", unit: "items", description: "Total number of messages dequeued from outbox.");

    internal static readonly UpDownCounter<long> OutboxInflight =
        Meter.CreateUpDownCounter<long>("bravellian.platform.outbox.inflight", unit: "items", description: "Number of in-flight outbox messages.");

    // Inbox
    internal static readonly Counter<long> InboxReceivedTotal =
        Meter.CreateCounter<long>("bravellian.platform.inbox.received_total", unit: "items", description: "Total number of messages received by inbox.");

    internal static readonly Counter<long> InboxProcessedTotal =
        Meter.CreateCounter<long>("bravellian.platform.inbox.processed_total", unit: "items", description: "Total number of messages processed by inbox.");

    internal static readonly Counter<long> InboxDeadletteredTotal =
        Meter.CreateCounter<long>("bravellian.platform.inbox.deadlettered_total", unit: "items", description: "Total number of messages dead-lettered.");

    // Processing Loop / QoS
    internal static readonly Counter<long> QosRetryTotal =
        Meter.CreateCounter<long>("bravellian.platform.qos.retry_total", unit: "items", description: "Total number of QoS retries.");

    internal static readonly Histogram<double> QosRetryDelay =
        Meter.CreateHistogram<double>("bravellian.platform.qos.retry_delay", unit: "s", description: "QoS retry delay duration.");
}
