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

using System.Data;
using System.Reflection;
using Bravellian.Platform.Outbox;

namespace Bravellian.Platform.Email.Tests;

internal sealed class InMemoryOutboxHarness : IOutbox, IOutboxStore
{
    private readonly List<Entry> entries = new();
    private readonly ManualTimeProvider timeProvider;

    public InMemoryOutboxHarness(ManualTimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public int DispatchedCount => entries.Count(entry => entry.Dispatched);

    public int FailedCount => entries.Count(entry => entry.Failed);

    public int EnqueuedCount => entries.Count;

    public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId: null, dueTimeUtc: null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId, null, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
    {
        entries.Add(new Entry
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
            DueTimeUtc = dueTimeUtc
        });

        return Task.CompletedTask;
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId, cancellationToken);
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
    {
        return EnqueueAsync(topic, payload, correlationId, dueTimeUtc, cancellationToken);
    }

    public Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Claiming by lease is not supported in tests.");
    }

    public Task AckAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Ack is not supported in tests.");
    }

    public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Abandon is not supported in tests.");
    }

    public Task FailAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Fail is not supported in tests.");
    }

    public Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Reap is not supported in tests.");
    }

    public Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Joins are not supported in tests.");
    }

    public Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Joins are not supported in tests.");
    }

    public Task ReportStepCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Joins are not supported in tests.");
    }

    public Task ReportStepFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Joins are not supported in tests.");
    }

    public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = entries
            .Where(entry => !entry.Dispatched && !entry.Failed)
            .Where(entry => entry.DueTimeUtc == null || entry.DueTimeUtc <= now)
            .Where(entry => !entry.Claimed)
            .Take(limit)
            .ToList();

        foreach (var entry in due)
        {
            entry.Claimed = true;
        }

        var messages = due.Select(CreateMessage).ToList();
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages);
    }

    public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
    {
        var entry = FindEntry(id);
        if (entry != null)
        {
            entry.Dispatched = true;
            entry.Claimed = false;
        }

        return Task.CompletedTask;
    }

    public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
    {
        var entry = FindEntry(id);
        if (entry != null)
        {
            entry.RetryCount++;
            entry.LastError = lastError;
            entry.DueTimeUtc = timeProvider.GetUtcNow().Add(delay);
            entry.Claimed = false;
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
    {
        var entry = FindEntry(id);
        if (entry != null)
        {
            entry.Failed = true;
            entry.LastError = lastError;
            entry.Claimed = false;
        }

        return Task.CompletedTask;
    }

    private OutboxMessage CreateMessage(Entry entry)
    {
        var message = new OutboxMessage
        {
            Id = CreateWorkItemIdentifier(entry.Id),
            Payload = entry.Payload,
            Topic = entry.Topic,
            CreatedAt = timeProvider.GetUtcNow(),
            RetryCount = entry.RetryCount,
            CorrelationId = entry.CorrelationId,
            DueTimeUtc = entry.DueTimeUtc
        };
        return message;
    }

    private static OutboxWorkItemIdentifier CreateWorkItemIdentifier(Guid value)
    {
        var type = typeof(OutboxWorkItemIdentifier);
        var ctor = type.GetConstructor(new[] { typeof(Guid) });
        if (ctor != null)
        {
            return (OutboxWorkItemIdentifier)ctor.Invoke(new object[] { value });
        }

        var instance = Activator.CreateInstance(type);
        var prop = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop?.SetValue(instance, value);
        return (OutboxWorkItemIdentifier)instance!;
    }

    private Entry? FindEntry(OutboxWorkItemIdentifier id)
    {
        var value = GetWorkItemGuid(id);
        return entries.FirstOrDefault(entry => entry.Id == value);
    }

    private static Guid GetWorkItemGuid(OutboxWorkItemIdentifier id)
    {
        var type = typeof(OutboxWorkItemIdentifier);
        var prop = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.PropertyType == typeof(Guid))
        {
            return (Guid)(prop.GetValue(id) ?? Guid.Empty);
        }

        return Guid.Empty;
    }

    private sealed class Entry
    {
        public Guid Id { get; set; }
        public string Topic { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? DueTimeUtc { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
        public bool Dispatched { get; set; }
        public bool Failed { get; set; }
        public bool Claimed { get; set; }
    }
}

