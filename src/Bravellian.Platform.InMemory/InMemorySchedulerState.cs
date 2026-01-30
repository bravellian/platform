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

using System.Linq;
using Cronos;

namespace Bravellian.Platform;

internal sealed class InMemorySchedulerState
{
    private const byte StatusReady = 0;
    private const byte StatusInProgress = 1;
    private const byte StatusDone = 2;
    private const byte StatusFailed = 3;

    private readonly Lock sync = new();
    private readonly Dictionary<Guid, TimerEntry> timers = new();
    private readonly Dictionary<string, JobEntry> jobsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, JobRunEntry> jobRuns = new();
    private readonly TimeProvider timeProvider;

    public InMemorySchedulerState(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string ScheduleTimer(string topic, string payload, DateTimeOffset dueTime)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            timers[id] = new TimerEntry
            {
                Id = id,
                Topic = topic,
                Payload = payload,
                DueTime = dueTime,
                StatusCode = StatusReady,
                CreatedAt = now,
            };
        }

        return id.ToString();
    }

    public bool CancelTimer(string timerId)
    {
        if (!Guid.TryParse(timerId, out var id))
        {
            return false;
        }

        lock (sync)
        {
            if (!timers.TryGetValue(id, out var entry))
            {
                return false;
            }

            if (entry.StatusCode != StatusReady)
            {
                return false;
            }

            entry.StatusCode = StatusFailed;
            entry.LastError = "Cancelled";
            return true;
        }
    }

    public void CreateOrUpdateJob(string jobName, string topic, string cronSchedule, string? payload)
    {
        var now = timeProvider.GetUtcNow();
        var nextDue = GetNextOccurrence(cronSchedule, now);

        lock (sync)
        {
            if (jobsByName.TryGetValue(jobName, out var entry))
            {
                entry.Topic = topic;
                entry.CronSchedule = cronSchedule;
                entry.Payload = payload;
                entry.NextDueTime = nextDue;
                return;
            }

            var job = new JobEntry
            {
                Id = Guid.NewGuid(),
                JobName = jobName,
                Topic = topic,
                CronSchedule = cronSchedule,
                Payload = payload,
                NextDueTime = nextDue,
            };

            jobsByName[jobName] = job;
        }
    }

    public void DeleteJob(string jobName)
    {
        lock (sync)
        {
            if (!jobsByName.TryGetValue(jobName, out var job))
            {
                return;
            }

            var runsToRemove = jobRuns.Values
                .Where(run => run.JobId == job.Id)
                .Select(run => run.Id)
                .ToList();

            foreach (var runId in runsToRemove)
            {
                jobRuns.Remove(runId);
            }

            jobsByName.Remove(jobName);
        }
    }

    public void TriggerJob(string jobName)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (!jobsByName.TryGetValue(jobName, out var job))
            {
                return;
            }

            var runId = Guid.NewGuid();
            jobRuns[runId] = new JobRunEntry
            {
                Id = runId,
                JobId = job.Id,
                ScheduledTime = now,
                StatusCode = StatusReady,
            };
        }
    }

    public IReadOnlyList<Guid> ClaimTimers(OwnerToken ownerToken, int leaseSeconds, int batchSize)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddSeconds(leaseSeconds);
        var claimed = new List<Guid>(batchSize);

        lock (sync)
        {
            foreach (var entry in timers.Values
                .Where(t => t.StatusCode == StatusReady)
                .Where(t => t.LockedUntil == null || t.LockedUntil <= now)
                .Where(t => t.DueTime <= now)
                .OrderBy(t => t.DueTime)
                .ThenBy(t => t.CreatedAt)
                .Take(batchSize))
            {
                entry.StatusCode = StatusInProgress;
                entry.OwnerToken = ownerToken;
                entry.LockedUntil = leaseUntil;
                claimed.Add(entry.Id);
            }
        }

        return claimed;
    }

    public IReadOnlyList<Guid> ClaimJobRuns(OwnerToken ownerToken, int leaseSeconds, int batchSize)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddSeconds(leaseSeconds);
        var claimed = new List<Guid>(batchSize);

        lock (sync)
        {
            foreach (var entry in jobRuns.Values
                .Where(t => t.StatusCode == StatusReady)
                .Where(t => t.LockedUntil == null || t.LockedUntil <= now)
                .Where(t => t.ScheduledTime <= now)
                .OrderBy(t => t.ScheduledTime)
                .Take(batchSize))
            {
                entry.StatusCode = StatusInProgress;
                entry.OwnerToken = ownerToken;
                entry.LockedUntil = leaseUntil;
                claimed.Add(entry.Id);
            }
        }

        return claimed;
    }

    public void AckTimers(OwnerToken ownerToken, IEnumerable<Guid> ids)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var id in ids)
            {
                if (timers.TryGetValue(id, out var entry)
                    && entry.StatusCode == StatusInProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.StatusCode = StatusDone;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.ProcessedAt = now;
                }
            }
        }
    }

    public void AckJobRuns(OwnerToken ownerToken, IEnumerable<Guid> ids)
    {
        lock (sync)
        {
            foreach (var id in ids)
            {
                if (jobRuns.TryGetValue(id, out var entry)
                    && entry.StatusCode == StatusInProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.StatusCode = StatusDone;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                }
            }
        }
    }

    public void AbandonTimers(OwnerToken ownerToken, IEnumerable<Guid> ids)
    {
        lock (sync)
        {
            foreach (var id in ids)
            {
                if (timers.TryGetValue(id, out var entry)
                    && entry.StatusCode == StatusInProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.StatusCode = StatusReady;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.RetryCount += 1;
                }
            }
        }
    }

    public void AbandonJobRuns(OwnerToken ownerToken, IEnumerable<Guid> ids)
    {
        lock (sync)
        {
            foreach (var id in ids)
            {
                if (jobRuns.TryGetValue(id, out var entry)
                    && entry.StatusCode == StatusInProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.StatusCode = StatusReady;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.RetryCount += 1;
                }
            }
        }
    }

    public int ReapExpiredTimers()
    {
        var now = timeProvider.GetUtcNow();
        var count = 0;

        lock (sync)
        {
            foreach (var entry in timers.Values)
            {
                if (entry.StatusCode == StatusInProgress
                    && entry.LockedUntil != null
                    && entry.LockedUntil <= now)
                {
                    entry.StatusCode = StatusReady;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    count++;
                }
            }
        }

        return count;
    }

    public int ReapExpiredJobRuns()
    {
        var now = timeProvider.GetUtcNow();
        var count = 0;

        lock (sync)
        {
            foreach (var entry in jobRuns.Values)
            {
                if (entry.StatusCode == StatusInProgress
                    && entry.LockedUntil != null
                    && entry.LockedUntil <= now)
                {
                    entry.StatusCode = StatusReady;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    count++;
                }
            }
        }

        return count;
    }

    public DateTimeOffset? GetNextEventTime()
    {
        lock (sync)
        {
            var nextTimer = timers.Values
                .Where(t => t.StatusCode == StatusReady)
                .Select(t => (DateTimeOffset?)t.DueTime)
                .Min();

            var nextJobRun = jobRuns.Values
                .Where(t => t.StatusCode == StatusReady)
                .Select(t => (DateTimeOffset?)t.ScheduledTime)
                .Min();

            var nextJob = jobsByName.Values
                .Select(j => j.NextDueTime)
                .Min();

            return new[] { nextTimer, nextJobRun, nextJob }
                .Where(t => t != null)
                .Min();
        }
    }

    public int CreateJobRunsFromDueJobs()
    {
        var now = timeProvider.GetUtcNow();
        var created = 0;

        lock (sync)
        {
            foreach (var job in jobsByName.Values
                .Where(j => j.NextDueTime != null && j.NextDueTime <= now)
                .ToList())
            {
                var runId = Guid.NewGuid();
                jobRuns[runId] = new JobRunEntry
                {
                    Id = runId,
                    JobId = job.Id,
                    ScheduledTime = now,
                    StatusCode = StatusReady,
                };
                created++;

                job.NextDueTime = GetNextOccurrence(job.CronSchedule, now);
            }
        }

        return created;
    }

    public IReadOnlyList<(Guid Id, string Topic, string Payload)> GetClaimedTimers(IEnumerable<Guid> ids)
    {
        lock (sync)
        {
            return ids
                .Select(id => timers.TryGetValue(id, out var entry) ? entry : null)
                .Where(entry => entry != null)
                .Select(entry => (entry!.Id, entry.Topic, entry.Payload))
                .ToList();
        }
    }

    public IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)> GetClaimedJobRuns(IEnumerable<Guid> ids)
    {
        lock (sync)
        {
            return ids
                .Select(id => jobRuns.TryGetValue(id, out var entry) ? entry : null)
                .Where(entry => entry != null)
                .Select(entry =>
                {
                    var job = jobsByName.Values.FirstOrDefault(j => j.Id == entry!.JobId);
                    return (entry!.Id, entry.JobId, job?.Topic ?? string.Empty, job?.Payload ?? string.Empty);
                })
                .ToList();
        }
    }

    private static DateTimeOffset? GetNextOccurrence(string cronSchedule, DateTimeOffset from)
    {
        var format = cronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        var expression = CronExpression.Parse(cronSchedule, format);
        var next = expression.GetNextOccurrence(from.UtcDateTime, TimeZoneInfo.Utc);
        return next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
    }

    private sealed class TimerEntry
    {
        public Guid Id { get; init; }

        public required string Topic { get; init; }

        public required string Payload { get; init; }

        public DateTimeOffset DueTime { get; init; }

        public byte StatusCode { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public OwnerToken? OwnerToken { get; set; }

        public int RetryCount { get; set; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? ProcessedAt { get; set; }

        public string? LastError { get; set; }
    }

    private sealed class JobEntry
    {
        public Guid Id { get; init; }

        public required string JobName { get; init; }

        public required string Topic { get; set; }

        public required string CronSchedule { get; set; }

        public string? Payload { get; set; }

        public DateTimeOffset? NextDueTime { get; set; }
    }

    private sealed class JobRunEntry
    {
        public Guid Id { get; init; }

        public Guid JobId { get; init; }

        public DateTimeOffset ScheduledTime { get; init; }

        public byte StatusCode { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public OwnerToken? OwnerToken { get; set; }

        public int RetryCount { get; set; }

        public string? LastError { get; set; }
    }
}
