# Outbox Examples

These examples show how to publish reliable background work using the outbox with the integrated work-queue primitives.

## 1) Transactional event publication

Wrap domain changes and outbox enqueue in the same transaction so handlers only see committed state.

```csharp
public async Task PlaceOrderAsync(Order order)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var tx = await connection.BeginTransactionAsync();

    await SaveOrderAsync(order, connection, tx);

    var message = new OutboxMessage(
        topic: "orders.placed",
        payload: JsonSerializer.Serialize(order));

    await outbox.EnqueueAsync(message.Topic, message.Payload, (SqlTransaction)tx, correlationId: order.Id.ToString());

    await tx.CommitAsync();
}
```

The outbox worker claims messages with leases and dispatches to handlers. If the worker crashes, `ReapExpired` returns messages to `Ready` for another worker.

## 2) Fanout to multiple destinations

Combine outbox with fanout policies to drive multiple integrations from a single enqueue.

```csharp
services.AddFanoutPolicy("orders.placed")
    .ToTopic("billing.charge")
    .ToTopic("notifications.email")
    .ToTopic("analytics.track");
```

- The fanout dispatcher claims outbox rows, honors leases, and enqueues downstream messages.
- Downstream handlers reuse the same `Claim → Ack/Abandon/Fail` lifecycle.
- Use `Outbox_Ack` to automatically advance fan-in joins when all downstream steps finish.

## 3) Handling poison messages

If a handler fails consistently, mark the row as failed to stop endless retries while keeping observability.

```csharp
if (attempts > 5)
{
    await outbox.FailAsync(ownerToken, new[] { id }, stoppingToken);
    continue;
}
```

Pair this with alerts on `Status = Failed` and a runbook to requeue after remediation.

## 4) Multi-tenant dispatch with dynamic discovery

Use `AddDynamicMultiSqlOutbox` when tenants are added at runtime.

```csharp
services.AddSingleton<IOutboxDatabaseDiscovery, GlobalDatabaseOutboxDiscovery>();
services.AddDynamicMultiSqlOutbox(
    selectionStrategy: new RoundRobinOutboxSelectionStrategy(),
    refreshInterval: TimeSpan.FromMinutes(5));
```

- New tenant databases appear automatically on the next refresh.
- The dispatcher respects per-tenant leases so noisy neighbors cannot starve others.
- Pair with [Dynamic Inbox Configuration](dynamic-inbox-example.md) when consumers also vary per tenant.

## 5) Observability and backpressure

Monitor claim and ack rates to detect lagging handlers. Adjust batch size or lease duration when:

- **High claim, low ack**: handler failures or downstream dependency issues; expect more `Abandon` and `ReapExpired` activity.
- **Low claim, high ready backlog**: increase batch size or polling frequency.
- **Consistent lease expiry**: lengthen `leaseSeconds` to match handler latency.

## Related guides

- [Outbox Quick Start](outbox-quickstart.md)
- [Work Queue Implementation](work-queue-implementation.md)
- [Platform Primitives Overview](platform-primitives-overview.md)
- [Lease Examples](lease-examples.md)
