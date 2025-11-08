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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that continuously monitors platform health and raises alerts.
/// </summary>
internal sealed class WatchdogService : BackgroundService, IWatchdog
{
    private readonly ILogger<WatchdogService> logger;
    private readonly IOptions<ObservabilityOptions> options;
    private readonly TimeProvider timeProvider;
    private readonly IEnumerable<IWatchdogAlertSink> alertSinks;
    private readonly IEnumerable<IHeartbeatSink> heartbeatSinks;
    private readonly ISchedulerState? schedulerState;
    private readonly IInboxState? inboxState;
    private readonly IOutboxState? outboxState;
    private readonly IProcessingState? processingState;

    private readonly ConcurrentDictionary<string, AlertEntry> activeAlerts = new();
    private DateTimeOffset lastScanAt;
    private DateTimeOffset lastHeartbeatAt;
    private long heartbeatSequence;

    public WatchdogService(
        ILogger<WatchdogService> logger,
        IOptions<ObservabilityOptions> options,
        TimeProvider timeProvider,
        IEnumerable<IWatchdogAlertSink> alertSinks,
        IEnumerable<IHeartbeatSink> heartbeatSinks,
        ISchedulerState? schedulerState = null,
        IInboxState? inboxState = null,
        IOutboxState? outboxState = null,
        IProcessingState? processingState = null)
    {
        this.logger = logger;
        this.options = options;
        this.timeProvider = timeProvider;
        this.alertSinks = alertSinks;
        this.heartbeatSinks = heartbeatSinks;
        this.schedulerState = schedulerState;
        this.inboxState = inboxState;
        this.outboxState = outboxState;
        this.processingState = processingState;

        this.lastScanAt = timeProvider.GetUtcNow();
        this.lastHeartbeatAt = timeProvider.GetUtcNow();
    }

    public WatchdogSnapshot GetSnapshot()
    {
        var alerts = this.activeAlerts.Values
            .Select(e => new ActiveAlert(
                e.Kind,
                e.Component,
                e.Key,
                e.Message,
                e.FirstSeenAt,
                e.LastSeenAt,
                e.Attributes))
            .ToList();

        return new WatchdogSnapshot(this.lastScanAt, this.lastHeartbeatAt, alerts);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = this.options.Value;
        var random = new Random();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Emit heartbeat if needed
                await this.EmitHeartbeatIfDueAsync(stoppingToken).ConfigureAwait(false);

                // Run watchdog scan
                await this.RunScanAsync(stoppingToken).ConfigureAwait(false);

                // Calculate jittered delay (±10%)
                var baseDelay = opts.Watchdog.ScanPeriod.TotalMilliseconds;
                var jitter = baseDelay * 0.1 * (random.NextDouble() * 2 - 1); // -10% to +10%
                var delay = TimeSpan.FromMilliseconds(baseDelay + jitter);

                // Use TimeProvider-aware delay that works with FakeTimeProvider
                await this.DelayAsync(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                this.logger.LogError(ex, "Watchdog scan failed.");
                await this.DelayAsync(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        // Use TimeProvider.CreateTimer to create a delay that respects fake time
        var tcs = new TaskCompletionSource<bool>();
        
        // Register cancellation
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        // Create timer that will complete the task after the delay
        var timer = this.timeProvider.CreateTimer(
            _ =>
            {
                tcs.TrySetResult(true);
            },
            null,
            delay,
            Timeout.InfiniteTimeSpan);

        // Clean up when task completes
        _ = tcs.Task.ContinueWith(
            _ =>
            {
                timer.Dispose();
                registration.Dispose();
            },
            TaskScheduler.Default);

        return tcs.Task;
    }

    private async Task EmitHeartbeatIfDueAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var opts = this.options.Value;

        if (now - this.lastHeartbeatAt >= opts.Watchdog.HeartbeatPeriod)
        {
            this.lastHeartbeatAt = now;
            this.heartbeatSequence++;

            // Emit metric
            PlatformMeters.WatchdogHeartbeatTotal.Add(1);

            // Invoke sinks
            var context = new HeartbeatContext(now, this.heartbeatSequence);
            foreach (var sink in this.heartbeatSinks)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5)); // Time-box callbacks
                    await sink.OnHeartbeatAsync(context, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    this.logger.LogWarning(ex, "Heartbeat sink failed.");
                }
            }

            if (opts.EnableLogging)
            {
                this.logger.LogInformation("Watchdog heartbeat #{Sequence} emitted.", this.heartbeatSequence);
            }
        }
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        this.lastScanAt = this.timeProvider.GetUtcNow();
        var opts = this.options.Value;

        var detectedAlerts = new List<DetectedAlert>();

        // Check scheduler
        if (this.schedulerState != null)
        {
            var overdueJobs = await this.schedulerState.GetOverdueJobsAsync(opts.Watchdog.JobOverdueThreshold, cancellationToken).ConfigureAwait(false);
            foreach (var (jobId, dueTime) in overdueJobs)
            {
                var delay = this.timeProvider.GetUtcNow() - dueTime;
                detectedAlerts.Add(new DetectedAlert
                {
                    Kind = WatchdogAlertKind.OverdueJob,
                    Component = "scheduler",
                    Key = $"job:{jobId}",
                    Message = $"Job {jobId} is overdue by {delay.TotalSeconds:F0} seconds.",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["job_id"] = jobId,
                        ["due_time"] = dueTime,
                        ["delay_seconds"] = delay.TotalSeconds,
                    },
                });
            }
        }

        // Check inbox
        if (this.inboxState != null)
        {
            var stuckMessages = await this.inboxState.GetStuckMessagesAsync(opts.Watchdog.InboxStuckThreshold, cancellationToken).ConfigureAwait(false);
            foreach (var (messageId, queue, receivedAt) in stuckMessages)
            {
                var age = this.timeProvider.GetUtcNow() - receivedAt;
                detectedAlerts.Add(new DetectedAlert
                {
                    Kind = WatchdogAlertKind.StuckInbox,
                    Component = "inbox",
                    Key = $"inbox:{queue}:{messageId}",
                    Message = $"Inbox message {messageId} in queue {queue} is stuck for {age.TotalMinutes:F0} minutes.",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["message_id"] = messageId,
                        ["queue"] = queue,
                        ["received_at"] = receivedAt,
                        ["age_minutes"] = age.TotalMinutes,
                    },
                });
            }
        }

        // Check outbox
        if (this.outboxState != null)
        {
            var stuckMessages = await this.outboxState.GetStuckMessagesAsync(opts.Watchdog.OutboxStuckThreshold, cancellationToken).ConfigureAwait(false);
            foreach (var (messageId, queue, createdAt) in stuckMessages)
            {
                var age = this.timeProvider.GetUtcNow() - createdAt;
                detectedAlerts.Add(new DetectedAlert
                {
                    Kind = WatchdogAlertKind.StuckOutbox,
                    Component = "outbox",
                    Key = $"outbox:{queue}:{messageId}",
                    Message = $"Outbox message {messageId} in queue {queue} is stuck for {age.TotalMinutes:F0} minutes.",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["message_id"] = messageId,
                        ["queue"] = queue,
                        ["created_at"] = createdAt,
                        ["age_minutes"] = age.TotalMinutes,
                    },
                });
            }
        }

        // Check processors
        if (this.processingState != null)
        {
            var idleProcessors = await this.processingState.GetIdleProcessorsAsync(opts.Watchdog.ProcessorIdleThreshold, cancellationToken).ConfigureAwait(false);
            foreach (var (processorId, component, lastActivityAt) in idleProcessors)
            {
                var idleTime = this.timeProvider.GetUtcNow() - lastActivityAt;
                detectedAlerts.Add(new DetectedAlert
                {
                    Kind = WatchdogAlertKind.ProcessorNotRunning,
                    Component = component,
                    Key = $"processor:{component}:{processorId}",
                    Message = $"Processor {processorId} in {component} has been idle for {idleTime.TotalMinutes:F0} minutes.",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["processor_id"] = processorId,
                        ["component"] = component,
                        ["last_activity_at"] = lastActivityAt,
                        ["idle_minutes"] = idleTime.TotalMinutes,
                    },
                });
            }
        }

        // Process alerts
        await this.ProcessAlertsAsync(detectedAlerts, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessAlertsAsync(List<DetectedAlert> detectedAlerts, CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var opts = this.options.Value;
        var detectedKeys = new HashSet<string>(detectedAlerts.Select(a => a.Key));

        // Raise or update alerts
        foreach (var alert in detectedAlerts)
        {
            var isNew = false;
            var entry = this.activeAlerts.AddOrUpdate(
                alert.Key,
                key =>
                {
                    isNew = true;
                    return new AlertEntry
                    {
                        Kind = alert.Kind,
                        Component = alert.Component,
                        Key = alert.Key,
                        Message = alert.Message,
                        Attributes = alert.Attributes,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        LastEmittedAt = now,
                    };
                },
                (key, existing) =>
                {
                    existing.LastSeenAt = now;
                    existing.Message = alert.Message;
                    existing.Attributes = alert.Attributes;
                    return existing;
                });

            // Emit if new or cooldown expired
            var shouldEmit = isNew || (now - entry.LastEmittedAt >= opts.Watchdog.AlertCooldown);
            if (shouldEmit)
            {
                entry.LastEmittedAt = now;

                // Emit metric
                PlatformMeters.WatchdogAlertsTotal.Add(1, new KeyValuePair<string, object?>("kind", alert.Kind.ToString()), new KeyValuePair<string, object?>("component", alert.Component));

                // Invoke sinks
                var context = new WatchdogAlertContext(
                    alert.Kind,
                    alert.Component,
                    alert.Key,
                    alert.Message,
                    entry.FirstSeenAt,
                    entry.LastSeenAt,
                    alert.Attributes);

                foreach (var sink in this.alertSinks)
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(TimeSpan.FromSeconds(5)); // Time-box callbacks
                        await sink.OnAlertAsync(context, cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        this.logger.LogWarning(ex, "Alert sink failed for alert {AlertKey}.", alert.Key);
                    }
                }

                if (opts.EnableLogging && isNew)
                {
                    this.logger.LogWarning("Watchdog alert raised: {AlertKind} - {Message}", alert.Kind, alert.Message);
                }
            }
        }

        // Remove resolved alerts
        foreach (var kvp in this.activeAlerts.Where(kvp => !detectedKeys.Contains(kvp.Key)))
        {
            if (this.activeAlerts.TryRemove(kvp.Key, out var removed) && opts.EnableLogging)
            {
                this.logger.LogInformation("Watchdog alert resolved: {AlertKind} - {Message}", removed.Kind, removed.Message);
            }
        }
    }

    private class DetectedAlert
    {
        public WatchdogAlertKind Kind { get; init; }

        public string Component { get; init; } = string.Empty;

        public string Key { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();
    }

    private class AlertEntry
    {
        public WatchdogAlertKind Kind { get; init; }

        public string Component { get; init; } = string.Empty;

        public string Key { get; init; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset LastSeenAt { get; set; }

        public DateTimeOffset LastEmittedAt { get; set; }

        public IReadOnlyDictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();
    }
}
