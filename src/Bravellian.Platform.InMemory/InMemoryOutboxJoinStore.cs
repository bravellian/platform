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

internal sealed class InMemoryOutboxJoinStore : IOutboxJoinStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<JoinIdentifier, OutboxJoin> joins = new();
    private readonly Dictionary<(JoinIdentifier JoinId, OutboxMessageIdentifier MessageId), OutboxJoinMember> members = new();
    private readonly TimeProvider timeProvider;

    public InMemoryOutboxJoinStore(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<OutboxJoin> CreateJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var join = new OutboxJoin
        {
            JoinId = JoinIdentifier.From(Guid.NewGuid()),
            TenantId = tenantId,
            ExpectedSteps = expectedSteps,
            CompletedSteps = 0,
            FailedSteps = 0,
            Status = 0,
            CreatedUtc = now,
            LastUpdatedUtc = now,
            Metadata = metadata,
        };

        lock (sync)
        {
            joins[join.JoinId] = join;
        }

        return Task.FromResult(join);
    }

    public Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (!joins.ContainsKey(joinId))
            {
                throw new InvalidOperationException($"Join {joinId} not found");
            }

            var key = (joinId, outboxMessageId);
            if (!members.ContainsKey(key))
            {
                members[key] = new OutboxJoinMember
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                    CreatedUtc = now,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<OutboxJoin?> GetJoinAsync(JoinIdentifier joinId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            return Task.FromResult(joins.TryGetValue(joinId, out var join) ? join : null);
        }
    }

    public Task<OutboxJoin> IncrementCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (!joins.TryGetValue(joinId, out var join))
            {
                throw new InvalidOperationException($"Join {joinId} not found");
            }

            var key = (joinId, outboxMessageId);
            if (!members.TryGetValue(key, out var member))
            {
                member = new OutboxJoinMember
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                    CreatedUtc = now,
                };
            }

            if (member.CompletedAt == null && member.FailedAt == null)
            {
                member = member with { CompletedAt = now };
                members[key] = member;

                if (join.CompletedSteps + join.FailedSteps < join.ExpectedSteps)
                {
                    join = join with
                    {
                        CompletedSteps = join.CompletedSteps + 1,
                        LastUpdatedUtc = now,
                    };
                    joins[joinId] = join;
                }
            }

            return Task.FromResult(join);
        }
    }

    public Task<OutboxJoin> IncrementFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (!joins.TryGetValue(joinId, out var join))
            {
                throw new InvalidOperationException($"Join {joinId} not found");
            }

            var key = (joinId, outboxMessageId);
            if (!members.TryGetValue(key, out var member))
            {
                member = new OutboxJoinMember
                {
                    JoinId = joinId,
                    OutboxMessageId = outboxMessageId,
                    CreatedUtc = now,
                };
            }

            if (member.CompletedAt == null && member.FailedAt == null)
            {
                member = member with { FailedAt = now };
                members[key] = member;

                if (join.CompletedSteps + join.FailedSteps < join.ExpectedSteps)
                {
                    join = join with
                    {
                        FailedSteps = join.FailedSteps + 1,
                        LastUpdatedUtc = now,
                    };
                    joins[joinId] = join;
                }
            }

            return Task.FromResult(join);
        }
    }

    public Task UpdateStatusAsync(JoinIdentifier joinId, byte status, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            if (joins.TryGetValue(joinId, out var join))
            {
                joins[joinId] = join with { Status = status, LastUpdatedUtc = now };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessageIdentifier>> GetJoinMessagesAsync(JoinIdentifier joinId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            var result = members.Keys
                .Where(key => key.JoinId.Equals(joinId))
                .Select(key => key.MessageId)
                .ToList();

            return Task.FromResult<IReadOnlyList<OutboxMessageIdentifier>>(result);
        }
    }

    internal void MarkMessageCompleted(OutboxMessageIdentifier outboxMessageId)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var key in members.Keys.Where(k => k.MessageId.Equals(outboxMessageId)).ToList())
            {
                if (!members.TryGetValue(key, out var member))
                {
                    continue;
                }

                if (member.CompletedAt == null && member.FailedAt == null)
                {
                    members[key] = member with { CompletedAt = now };
                    UpdateJoinCounter(key.JoinId, now, completed: true);
                }
            }
        }
    }

    internal void MarkMessageFailed(OutboxMessageIdentifier outboxMessageId)
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            foreach (var key in members.Keys.Where(k => k.MessageId.Equals(outboxMessageId)).ToList())
            {
                if (!members.TryGetValue(key, out var member))
                {
                    continue;
                }

                if (member.CompletedAt == null && member.FailedAt == null)
                {
                    members[key] = member with { FailedAt = now };
                    UpdateJoinCounter(key.JoinId, now, completed: false);
                }
            }
        }
    }

    private void UpdateJoinCounter(JoinIdentifier joinId, DateTimeOffset now, bool completed)
    {
        if (!joins.TryGetValue(joinId, out var join))
        {
            return;
        }

        if (join.CompletedSteps + join.FailedSteps >= join.ExpectedSteps)
        {
            return;
        }

        joins[joinId] = completed
            ? join with { CompletedSteps = join.CompletedSteps + 1, LastUpdatedUtc = now }
            : join with { FailedSteps = join.FailedSteps + 1, LastUpdatedUtc = now };
    }
}
