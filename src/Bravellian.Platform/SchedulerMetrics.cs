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
using System.Diagnostics.Metrics;

internal static class SchedulerMetrics
{
    private static readonly Meter Meter = new ("YourApplication.Scheduler", "1.0.0");

    // Counters: Things that only go up
    public static readonly Counter<long> TimersDispatched = Meter.CreateCounter<long>("scheduler.timers.dispatched.count", "timers", "Number of timers dispatched for execution.");
    public static readonly Counter<long> JobsDispatched = Meter.CreateCounter<long>("scheduler.jobs.dispatched.count", "jobs", "Number of jobs dispatched for execution.");
    public static readonly Counter<long> OutboxMessagesSent = Meter.CreateCounter<long>("scheduler.outbox.sent.count", "messages", "Number of outbox messages successfully sent.");
    public static readonly Counter<long> OutboxMessagesFailed = Meter.CreateCounter<long>("scheduler.outbox.failed.count", "messages", "Number of outbox messages that failed to send.");

    // Histograms: To measure duration
    public static readonly Histogram<double> OutboxSendDuration = Meter.CreateHistogram<double>("scheduler.outbox.send.duration", "ms", "Duration of sending a message from the outbox.");

    // Gauges: To report current state
    static SchedulerMetrics()
    {
        Meter.CreateObservableGauge("scheduler.outbox.pending.gauge", () => GetPendingCount("dbo.Outbox", "IsProcessed = 0"), "messages", "Number of pending messages in the outbox.");
        Meter.CreateObservableGauge("scheduler.timers.pending.gauge", () => GetPendingCount("dbo.Timers", "Status = 'Pending'"), "timers", "Number of pending timers.");
        Meter.CreateObservableGauge("scheduler.jobs.pending.gauge", () => GetPendingCount("dbo.JobRuns", "Status = 'Pending'"), "jobs", "Number of pending job runs.");
    }

    private static long GetPendingCount(string table, string whereClause)
    {
        // This method would contain the Dapper logic to run:
        // SELECT COUNT(*) FROM {table} WHERE {whereClause}
        // NOTE: This should be implemented with a connection string.
        // For brevity, the implementation is omitted here.
        return 0; // Replace with actual DB call
    }
}
