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
using Bravellian.Platform.Outbox;

namespace Bravellian.Platform;

internal sealed class InMemoryOutboxState
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, OutboxEntry> entries = new();
    private readonly TimeProvider timeProvider;

    public InMemoryOutboxState(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Guid Enqueue(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc)
    {
        var now = timeProvider.GetUtcNow();
        var workItemId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var entry = new OutboxEntry
        {
            Id = workItemId,
            MessageId = messageId,
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
            CreatedAt = now,
            DueTimeUtc = dueTimeUtc,
            Status = OutboxStatus.Ready,
            RetryCount = 0,
            IsProcessed = false,
        };

        lock (sync)
        {
            entries[workItemId] = entry;
        }

        return workItemId;
    }

    public IReadOnlyList<OutboxWorkItemIdentifier> Claim(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddSeconds(leaseSeconds);
        var claimed = new List<OutboxWorkItemIdentifier>(batchSize);

        lock (sync)
        {
            foreach (var entry in entries.Values
                .Where(e => e.Status == OutboxStatus.Ready)
                .Where(e => e.LockedUntil == null || e.LockedUntil <= now)
                .Where(e => e.DueTimeUtc == null || e.DueTimeUtc <= now)
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize))
            {
                entry.Status = OutboxStatus.InProgress;
                entry.OwnerToken = ownerToken;
                entry.LockedUntil = leaseUntil;
                claimed.Add(OutboxWorkItemIdentifier.From(entry.Id));
            }
        }

        return claimed;
    }

    public IReadOnlyList<OutboxMessage> ClaimDue(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.AddSeconds(leaseSeconds);
        var claimed = new List<OutboxMessage>(batchSize);

        lock (sync)
        {
            foreach (var entry in entries.Values
                .Where(e => e.Status == OutboxStatus.Ready)
                .Where(e => e.LockedUntil == null || e.LockedUntil <= now)
                .Where(e => e.DueTimeUtc == null || e.DueTimeUtc <= now)
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize))
            {
                entry.Status = OutboxStatus.InProgress;
                entry.OwnerToken = ownerToken;
                entry.LockedUntil = leaseUntil;
                claimed.Add(ToMessage(entry));
            }
        }

        return claimed;
    }

    public void Ack(OwnerToken ownerToken, IEnumerable<Guid> ids)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var id in ids)
            {
                if (entries.TryGetValue(id, out var entry)
                    && entry.Status == OutboxStatus.InProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = OutboxStatus.Done;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.IsProcessed = true;
                    entry.ProcessedAt = now;
                }
            }
        }
    }

    public void Abandon(OwnerToken ownerToken, IEnumerable<Guid> ids, string? lastError, DateTimeOffset? dueTimeUtc)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var id in ids)
            {
                if (entries.TryGetValue(id, out var entry)
                    && entry.Status == OutboxStatus.InProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = OutboxStatus.Ready;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.RetryCount += 1;
                    entry.LastError = string.IsNullOrEmpty(lastError) ? entry.LastError : lastError;
                    entry.DueTimeUtc = dueTimeUtc ?? entry.DueTimeUtc ?? now;
                }
            }
        }
    }

    public void Fail(OwnerToken ownerToken, IEnumerable<Guid> ids, string? lastError, string? processedBy)
    {
        lock (sync)
        {
            foreach (var id in ids)
            {
                if (entries.TryGetValue(id, out var entry)
                    && entry.Status == OutboxStatus.InProgress
                    && entry.OwnerToken == ownerToken)
                {
                    entry.Status = OutboxStatus.Failed;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    entry.IsProcessed = false;
                    entry.LastError = string.IsNullOrEmpty(lastError) ? entry.LastError : lastError;
                    entry.ProcessedBy = string.IsNullOrEmpty(processedBy) ? entry.ProcessedBy : processedBy;
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
                if (entry.Status == OutboxStatus.InProgress
                    && entry.LockedUntil != null
                    && entry.LockedUntil <= now)
                {
                    entry.Status = OutboxStatus.Ready;
                    entry.OwnerToken = null;
                    entry.LockedUntil = null;
                    count++;
                }
            }
        }

        return count;
    }

    public int Cleanup(TimeSpan retentionPeriod)
    {
        var cutoff = timeProvider.GetUtcNow().Add(-retentionPeriod);
        var toRemove = new List<Guid>();

        lock (sync)
        {
            foreach (var entry in entries.Values)
            {
                if (entry.Status == OutboxStatus.Done
                    && entry.ProcessedAt != null
                    && entry.ProcessedAt < cutoff)
                {
                    toRemove.Add(entry.Id);
                }
            }

            foreach (var id in toRemove)
            {
                entries.Remove(id);
            }
        }

        return toRemove.Count;
    }

    public OutboxMessage? GetMessage(Guid id)
    {
        lock (sync)
        {
            return entries.TryGetValue(id, out var entry) ? ToMessage(entry) : null;
        }
    }

    private static OutboxMessage ToMessage(OutboxEntry entry)
    {
        return new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.From(entry.Id),
            MessageId = OutboxMessageIdentifier.From(entry.MessageId),
            Topic = entry.Topic,
            Payload = entry.Payload,
            CreatedAt = entry.CreatedAt,
            IsProcessed = entry.IsProcessed,
            ProcessedAt = entry.ProcessedAt,
            ProcessedBy = entry.ProcessedBy,
            RetryCount = entry.RetryCount,
            LastError = entry.LastError,
            CorrelationId = entry.CorrelationId,
            DueTimeUtc = entry.DueTimeUtc,
        };
    }

    private sealed class OutboxEntry
    {
        public Guid Id { get; init; }

        public Guid MessageId { get; init; }

        public required string Topic { get; init; }

        public required string Payload { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public bool IsProcessed { get; set; }

        public DateTimeOffset? ProcessedAt { get; set; }

        public string? ProcessedBy { get; set; }

        public int RetryCount { get; set; }

        public string? LastError { get; set; }

        public string? CorrelationId { get; init; }

        public DateTimeOffset? DueTimeUtc { get; set; }

        public byte Status { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public OwnerToken? OwnerToken { get; set; }
    }
}
