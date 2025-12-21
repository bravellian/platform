# Work Queue Implementation Details

This guide dives into how the platform implements claim/ack/abandon/fail semantics across outbox, inbox, timers, and jobs. Use it alongside [work-queue-pattern](work-queue-pattern.md) when you need to reason about locks, leases, and batching in production.

## Storage Layout

All work-queue-aware tables share the same columns:

- `Status` (`TINYINT`): `0=Ready`, `1=InProgress`, `2=Done`, `3=Failed`
- `LockedUntil` (`DATETIME2(3)`): lease expiration in UTC
- `OwnerToken` (`UNIQUEIDENTIFIER`): who claimed the row
- Retry and diagnostic columns are preserved per domain (outbox payload metadata, inbox source identifiers, timer due time, etc.).

**Indexes** prioritize claim order and avoid contention:

```sql
CREATE INDEX IX_Outbox_WorkQueue ON dbo.Outbox(Status, CreatedAt) INCLUDE (Id, OwnerToken);
CREATE INDEX IX_Inbox_WorkQueue ON dbo.Inbox(Status, DeliveredAt) INCLUDE (Id, OwnerToken);
CREATE INDEX IX_Timers_WorkQueue ON dbo.Timers(StatusCode, DueTime) INCLUDE (Id, OwnerToken);
CREATE INDEX IX_JobRuns_WorkQueue ON dbo.JobRuns(StatusCode, ScheduledTime) INCLUDE (Id, OwnerToken);
```

## Stored Procedures

Each table ships with a generated set of procedures. The outbox naming is shown; inbox/timers/jobs follow the same pattern.

- `Outbox_Claim` – atomically moves rows from `Ready` to `InProgress`, assigns `OwnerToken`, sets `LockedUntil = SYSUTCDATETIME() + @LeaseSeconds`, and returns the claimed IDs ordered by creation time.
- `Outbox_Ack` – marks rows as `Done` when the caller's `@OwnerToken` matches.
- `Outbox_Abandon` – resets status to `Ready` so another worker can retry after backoff.
- `Outbox_Fail` – moves to `Failed`, capturing error context in domain-specific columns.
- `Outbox_ReapExpired` – clears stale leases where `LockedUntil < SYSUTCDATETIME()` and `Status = InProgress`.

### Transactional properties

- **Short-lived claims**: `Outbox_Claim` uses `UPDLOCK, READPAST, ROWLOCK` to avoid blocking concurrent pollers.
- **Owner enforcement**: `Ack/Abandon/Fail` filter by `Id IN @Ids AND OwnerToken = @OwnerToken` to prevent accidental cross-worker updates.
- **Lease-first retry**: `ReapExpired` is safe to run frequently; it only affects expired rows.

## Cross-component behavior

| Component | Claim filter | Completion action | Failure/Retry | Expiry recovery |
|-----------|--------------|-------------------|---------------|-----------------|
| Outbox | `Status = Ready` | `Ack → Done` (updates joins/fan-in) | `Abandon → Ready`, `Fail → Failed` | `ReapExpired` resets to `Ready` |
| Inbox | `Status = Ready` | `Ack → Done` after handler commits | `Abandon → Ready` (to retry idempotent handler) | `ReapExpired` resets to `Ready` |
| Timers | `Status = Ready AND DueTime <= now()` | `AckTimers → Done` | `AbandonTimers → Ready` | `ReapExpiredTimers` resets |
| Job Runs | `Status = Ready AND ScheduledTime <= now()` | `AckJobRuns → Done` | `AbandonJobRuns → Ready` | `ReapExpiredJobRuns` resets |

## Handler patterns

### Batched outbox worker

```csharp
public class BatchedOutboxWorker : BackgroundService
{
    private readonly IOutbox outbox;
    private readonly Guid ownerToken = Guid.NewGuid();

    public BatchedOutboxWorker(IOutbox outbox) => this.outbox = outbox;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ids = await outbox.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 100, stoppingToken);
            if (ids.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            var succeeded = new List<Guid>();
            var toRetry = new List<Guid>();

            foreach (var id in ids)
            {
                try
                {
                    await ProcessMessageAsync(id, stoppingToken);
                    succeeded.Add(id);
                }
                catch (TransientException)
                {
                    toRetry.Add(id);
                }
            }

            if (succeeded.Count > 0)
                await outbox.AckAsync(ownerToken, succeeded, stoppingToken);

            if (toRetry.Count > 0)
                await outbox.AbandonAsync(ownerToken, toRetry, stoppingToken);
        }
    }
}
```

### Lease-aware scheduler loop

Timers and job runs often use smaller batches but stricter leases:

```csharp
var lease = TimeSpan.FromSeconds(15);
var timers = await scheduler.ClaimTimersAsync(ownerToken, (int)lease.TotalSeconds, batchSize: 10, stoppingToken);
// ...process...
await scheduler.AckTimersAsync(ownerToken, timers, stoppingToken);
```

Use `ReapExpired*` commands from a maintenance job every few minutes to reclaim abandoned work.

## Monitoring and tuning

- Track **claim rate vs. ack rate** per table; large deltas indicate stuck leases.
- Start with **batch sizes of 50–100** for outbox, **10–20** for inbox/timers, then tune based on handler latency.
- Set **lease length slightly above P95 handler time**; too long slows recovery, too short increases abandon noise.
- Alert when **`Status = InProgress` exceeds lease by 2x**; this points to crashed workers.

## Related documents

- [Work Queue Pattern](work-queue-pattern.md) – conceptual flow
- [Outbox Examples](outbox-examples.md) – end-to-end outbox handlers
- [Inbox Examples](inbox-examples.md) – idempotent consumer flows
- [Lease Examples](lease-examples.md) – distributed lock scenarios
