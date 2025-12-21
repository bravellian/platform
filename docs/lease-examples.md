# Lease Examples

These examples show how to use `LeaseManager` for distributed coordination alongside inbox/outbox work queues.

## 1) Leader election for scheduled jobs

Ensure only one instance runs a periodic job at a time.

```csharp
var handle = await leaseManager.TryAcquireAsync(
    name: "jobs/daily-maintenance",
    duration: TimeSpan.FromMinutes(2),
    renewalCadence: TimeSpan.FromSeconds(45),
    cancellationToken: ct);

if (handle is null)
    return; // another node is the leader

await using var _ = handle; // auto-renews until disposed
await RunDailyMaintenanceAsync(ct);
```

Pair this with a timer-based work queue so the job body still uses claim/ack semantics for sub-tasks.

## 2) Protecting fanout cursors

When multiple workers advance the same fanout cursor, guard updates with a lease to prevent duplicate dispatches.

```csharp
await using var cursorLease = await leaseManager.AcquireAsync(
    name: "fanout/policy:orders.placed",
    duration: TimeSpan.FromSeconds(30),
    renewalCadence: TimeSpan.FromSeconds(10),
    cancellationToken: ct);

await DispatchFanoutBatchAsync(ct);
```

If a node crashes, the lease expires and another worker resumes at the same cursor position.

## 3) Serializing schema migrations

Wrap migrations so only one environment actor mutates the schema at once, even if rollout scripts run in parallel.

```csharp
await using var migrationLease = await leaseManager.AcquireAsync(
    name: "schema/migrations/outbox",
    duration: TimeSpan.FromMinutes(10),
    renewalCadence: TimeSpan.FromMinutes(3),
    cancellationToken: ct);

await RunMigrationsAsync(ct);
```

## 4) Coordinating inbox + outbox handoff

Use a short-lived lease to ensure only one handler publishes a reply for a particular inbox message when retries overlap.

```csharp
await using var replyLease = await leaseManager.TryAcquireAsync(
    name: $"reply/{inboxRecord.MessageId}",
    duration: TimeSpan.FromSeconds(15),
    renewalCadence: TimeSpan.FromSeconds(5),
    cancellationToken: ct);

if (replyLease is null)
{
    await inbox.AbandonAsync(ownerToken, new[] { inboxRecord.Id }, ct);
    return; // another worker will produce the reply
}

await SendReplyAsync(inboxRecord, ct);
await inbox.AckAsync(ownerToken, new[] { inboxRecord.Id }, ct);
```

## 5) Throttling external API calls

Lease names can include a rate-limit scope to keep concurrent calls under control across the fleet.

```csharp
await using var throttle = await leaseManager.AcquireAsync(
    name: "external/payments/throttle",
    duration: TimeSpan.FromSeconds(5),
    renewalCadence: TimeSpan.FromSeconds(2),
    cancellationToken: ct);

await paymentGateway.ChargeAsync(request, ct);
```

## Operational guidance

- Default lease durations should be slightly longer than the expected critical section; rely on automatic renewal rather than long leases.
- Monitor lease acquisition failures to spot contention or runaway tasks.
- Use consistent naming (`component/purpose[/key]`) so dashboards and alerts can group related leases.

## Related guides

- [Lease System v2](lease-v2-usage.md)
- [Outbox Examples](outbox-examples.md)
- [Inbox Examples](inbox-examples.md)
- [Work Queue Implementation](work-queue-implementation.md)
