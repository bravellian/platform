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

namespace Bravellian.Platform;

internal sealed class InMemoryInboxState
{
    private const string StatusSeen = "Seen";
    private const string StatusProcessing = "Processing";
    private const string StatusDone = "Done";
    private const string StatusDead = "Dead";

    private readonly Lock sync = new();
    private readonly Dictionary<string, InboxEntry> entries = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    public InMemoryInboxState(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool AlreadyProcessed(string messageId, string source, byte[]? hash)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (entries.TryGetValue(messageId, out var entry))
            {
                entry.LastSeenUtc = now;
                entry.Attempts += 1;
                return entry.ProcessedUtc != null;
            }

            entries[messageId] = new InboxEntry
            {
                MessageId = messageId,
                Source = source,
                Hash = hash,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                Attempts = 1,
                Status = StatusSeen,
            };

            return false;
        }
    }

    public void Enqueue(string topic, string source, string messageId, string payload, byte[]? hash, DateTimeOffset? dueTimeUtc)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (entries.TryGetValue(messageId, out var entry))
            {
                entry.LastSeenUtc = now;
                entry.Attempts += 1;
                entry.Topic ??= topic;
                entry.Payload ??= payload;
                if (dueTimeUtc != null)
                {
                    entry.DueTimeUtc ??= dueTimeUtc;
                }

                return;
            }

            entries[messageId] = new InboxEntry
            {
                MessageId = messageId,
                Source = source,
                Topic = topic,
                Payload = payload,
                Hash = hash,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                DueTimeUtc = dueTimeUtc,
                Attempts = 1,
                Status = StatusSeen,
            };
        }
    }

    public void MarkProcessed(string messageId)
    {
        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            if (entries.TryGetValue(messageId, out var entry))
            {
                entry.Status = StatusDone;
                entry.ProcessedUtc = now;
                entry.LastSeenUtc = now;
            }
        }
    }

    public void MarkProcessing(string messageId)
    {
        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            if (entries.TryGetValue(messageId, out var entry))
            {
                entry.Status = StatusProcessing;
                entry.LastSeenUtc = now;
            }
        }
    }

    public void MarkDead(string messageId)
    {
        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            if (entries.TryGetValue(messageId, out var entry))
            {
                entry.Status = StatusDead;
                entry.LastSeenUtc = now;
            }
        }
    }

    public IReadOnlyList<string> Claim(OwnerToken ownerToken, int leaseSeconds, int batchSize)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddSeconds(leaseSeconds);
        var claimed = new List<string>(batchSize);

        lock (sync)
        {
            foreach (var entry in entries.Values
                .Where(e => string.Equals(e.Status, StatusSeen, StringComparison.Ordinal))
                .Where(e => e.LockedUntil == null || e.LockedUntil <= now)
                .Where(e => e.DueTimeUtc == null || e.DueTimeUtc <= now)
                .OrderBy(e => e.LastSeenUtc)
                .Take(batchSize))
            {
                entry.Status = StatusProcessing;
                entry.OwnerToken = ownerToken;
                entry.LockedUntil = leaseUntil;
                entry.Attempts += 1;
                entry.LastSeenUtc = now;
                claimed.Add(entry.MessageId);
            }
        }

        return claimed;
    }

    public void Ack(OwnerToken ownerToken, IEnumerable<string> messageIds)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var id in messageIds)
            {
                if (entries.TryGetValue(id, out var entry)
                    && string.Equals(entry.Status, StatusProcessing, StringComparison.Ordinal)
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = StatusDone;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.ProcessedUtc = now;
                    entry.LastSeenUtc = now;
                }
            }
        }
    }

    public void Abandon(OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError, TimeSpan? delay)
    {
        var now = timeProvider.GetUtcNow();
        var dueTime = delay.HasValue ? now.Add(delay.Value) : (DateTimeOffset?)null;

        lock (sync)
        {
            foreach (var id in messageIds)
            {
                if (entries.TryGetValue(id, out var entry)
                    && string.Equals(entry.Status, StatusProcessing, StringComparison.Ordinal)
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = StatusSeen;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.LastError = string.IsNullOrEmpty(lastError) ? entry.LastError : lastError;
                    entry.DueTimeUtc = dueTime ?? entry.DueTimeUtc;
                    entry.LastSeenUtc = now;
                }
            }
        }
    }

    public void Fail(OwnerToken ownerToken, IEnumerable<string> messageIds, string errorMessage)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var id in messageIds)
            {
                if (entries.TryGetValue(id, out var entry)
                    && string.Equals(entry.Status, StatusProcessing, StringComparison.Ordinal)
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = StatusDead;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.LastError = errorMessage;
                    entry.LastSeenUtc = now;
                }
            }
        }
    }

    public void Revive(IEnumerable<string> messageIds, string? reason, TimeSpan? delay)
    {
        if (delay.HasValue && delay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be non-negative when reviving inbox messages.");
        }

        var normalizedReason = NormalizeReason(reason);
        var now = timeProvider.GetUtcNow();
        var dueTime = delay.HasValue ? now.Add(delay.Value) : (DateTimeOffset?)null;

        lock (sync)
        {
            foreach (var id in messageIds)
            {
                if (entries.TryGetValue(id, out var entry)
                    && string.Equals(entry.Status, StatusDead, StringComparison.Ordinal))
                {
                    entry.Status = StatusSeen;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.LastError = normalizedReason;
                    entry.DueTimeUtc = dueTime;
                    entry.LastSeenUtc = now;
                }
            }
        }
    }

    public int ReapExpired()
    {
        var now = timeProvider.GetUtcNow();
        var count = 0;

        lock (sync)
        {
            foreach (var entry in entries.Values)
            {
                if (string.Equals(entry.Status, StatusProcessing, StringComparison.Ordinal)
                    && entry.LockedUntil != null
                    && entry.LockedUntil <= now)
                {
                    entry.Status = StatusSeen;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    count++;
                }
            }
        }

        return count;
    }

    public InboxMessage Get(string messageId)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(messageId, out var entry))
            {
                throw new InvalidOperationException($"Inbox message '{messageId}' not found");
            }

            return new InboxMessage
            {
                MessageId = InboxMessageIdentifier.From(entry.MessageId),
                Source = entry.Source,
                Topic = entry.Topic ?? string.Empty,
                Payload = entry.Payload ?? string.Empty,
                Hash = entry.Hash,
                Attempt = entry.Attempts,
                FirstSeenUtc = entry.FirstSeenUtc,
                LastSeenUtc = entry.LastSeenUtc,
                ProcessedUtc = entry.ProcessedUtc,
                DueTimeUtc = entry.DueTimeUtc,
                LastError = entry.LastError,
            };
        }
    }

    public int Cleanup(TimeSpan retentionPeriod)
    {
        var cutoff = timeProvider.GetUtcNow().Add(-retentionPeriod);
        var toRemove = new List<string>();

        lock (sync)
        {
            foreach (var entry in entries.Values)
            {
                if (string.Equals(entry.Status, StatusDone, StringComparison.Ordinal)
                    && entry.ProcessedUtc != null
                    && entry.ProcessedUtc < cutoff)
                {
                    toRemove.Add(entry.MessageId);
                }
            }

            foreach (var id in toRemove)
            {
                entries.Remove(id);
            }
        }

        return toRemove.Count;
    }

    private sealed class InboxEntry
    {
        public required string MessageId { get; init; }

        public required string Source { get; init; }

        public string? Topic { get; set; }

        public string? Payload { get; set; }

        public byte[]? Hash { get; init; }

        public DateTimeOffset FirstSeenUtc { get; init; }

        public DateTimeOffset LastSeenUtc { get; set; }

        public DateTimeOffset? ProcessedUtc { get; set; }

        public DateTimeOffset? DueTimeUtc { get; set; }

        public int Attempts { get; set; }

        public string Status { get; set; } = StatusSeen;

        public string? LastError { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public OwnerToken? OwnerToken { get; set; }
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
