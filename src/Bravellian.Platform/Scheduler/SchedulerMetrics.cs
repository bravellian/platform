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

namespace Bravellian.Platform;
using System.Diagnostics;
using System.Diagnostics.Metrics;

internal static class SchedulerMetrics
{
    private static readonly Meter Meter = new("Bravellian.Platform", "1.0.0");
    private static readonly ActivitySource ActivitySource = new("Bravellian.Platform");

    // Work Queue Counters
    public static readonly Counter<long> OutboxItemsClaimed = Meter.CreateCounter<long>("outbox.items.claimed", "items", "Number of outbox items claimed.");
    public static readonly Counter<long> OutboxItemsAcknowledged = Meter.CreateCounter<long>("outbox.items.acknowledged", "items", "Number of outbox items acknowledged.");
    public static readonly Counter<long> OutboxItemsAbandoned = Meter.CreateCounter<long>("outbox.items.abandoned", "items", "Number of outbox items abandoned.");
    public static readonly Counter<long> OutboxItemsFailed = Meter.CreateCounter<long>("outbox.items.failed", "items", "Number of outbox items marked as failed.");
    public static readonly Counter<long> OutboxItemsReaped = Meter.CreateCounter<long>("outbox.items.reaped", "items", "Number of expired outbox items reaped.");

    public static readonly Counter<long> TimerItemsClaimed = Meter.CreateCounter<long>("timers.items.claimed", "items", "Number of timer items claimed.");
    public static readonly Counter<long> TimerItemsAcknowledged = Meter.CreateCounter<long>("timers.items.acknowledged", "items", "Number of timer items acknowledged.");
    public static readonly Counter<long> TimerItemsAbandoned = Meter.CreateCounter<long>("timers.items.abandoned", "items", "Number of timer items abandoned.");
    public static readonly Counter<long> TimerItemsReaped = Meter.CreateCounter<long>("timers.items.reaped", "items", "Number of expired timer items reaped.");

    public static readonly Counter<long> JobRunItemsClaimed = Meter.CreateCounter<long>("jobruns.items.claimed", "items", "Number of job run items claimed.");
    public static readonly Counter<long> JobRunItemsAcknowledged = Meter.CreateCounter<long>("jobruns.items.acknowledged", "items", "Number of job run items acknowledged.");
    public static readonly Counter<long> JobRunItemsAbandoned = Meter.CreateCounter<long>("jobruns.items.abandoned", "items", "Number of job run items abandoned.");
    public static readonly Counter<long> JobRunItemsReaped = Meter.CreateCounter<long>("jobruns.items.reaped", "items", "Number of expired job run items reaped.");

    // Legacy Counters
    public static readonly Counter<long> TimersDispatched = Meter.CreateCounter<long>("scheduler.timers.dispatched.count", "timers", "Number of timers dispatched for execution.");
    public static readonly Counter<long> JobsDispatched = Meter.CreateCounter<long>("scheduler.jobs.dispatched.count", "jobs", "Number of jobs dispatched for execution.");
    public static readonly Counter<long> OutboxMessagesSent = Meter.CreateCounter<long>("scheduler.outbox.sent.count", "messages", "Number of outbox messages successfully sent.");
    public static readonly Counter<long> OutboxMessagesFailed = Meter.CreateCounter<long>("scheduler.outbox.failed.count", "messages", "Number of outbox messages that failed to send.");
    public static readonly Counter<long> LeasesAcquired = Meter.CreateCounter<long>("scheduler.leases.acquired.count", "leases", "Number of leases successfully acquired.");
    public static readonly Counter<long> LeasesLost = Meter.CreateCounter<long>("scheduler.leases.lost.count", "leases", "Number of leases that were lost.");

    // Histograms: To measure duration
    public static readonly Histogram<double> OutboxSendDuration = Meter.CreateHistogram<double>("scheduler.outbox.send.duration", "ms", "Duration of sending a message from the outbox.");
    public static readonly Histogram<double> LeaseRenewDuration = Meter.CreateHistogram<double>("scheduler.lease.renew.duration", "ms", "Duration of lease renewal operations.");

    // Work Queue Operation Durations
    public static readonly Histogram<double> WorkQueueClaimDuration = Meter.CreateHistogram<double>("workqueue.claim.duration", "ms", "Duration of work queue claim operations.");
    public static readonly Histogram<double> WorkQueueAckDuration = Meter.CreateHistogram<double>("workqueue.ack.duration", "ms", "Duration of work queue acknowledge operations.");
    public static readonly Histogram<double> WorkQueueAbandonDuration = Meter.CreateHistogram<double>("workqueue.abandon.duration", "ms", "Duration of work queue abandon operations.");

    // Gauges: To report current state
    // Note: Observable gauges for pending counts have been removed as they require
    // schema-specific configuration and active database connections. Applications
    // should implement these metrics in their own services with appropriate schema context.

    /// <summary>
    /// Starts a new activity for tracing work queue operations.
    /// </summary>
    /// <param name="operationName">The name of the operation.</param>
    /// <returns>The started activity, or null if not enabled.</returns>
    public static Activity? StartActivity(string operationName)
    {
        return ActivitySource.StartActivity(operationName);
    }
}
