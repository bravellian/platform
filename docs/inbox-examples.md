# Inbox Examples

These scenarios illustrate how to use the inbox to guarantee idempotent consumption with the shared work-queue lifecycle.

## 1) Deduplicated webhook processing

```csharp
public async Task HandleWebhookAsync(WebhookEvent webhook, CancellationToken ct)
{
    // Enqueue receipt; duplicates collapse on MessageId + Source
    await inbox.EnqueueAsync(new InboxMessage(
        messageId: webhook.Id,
        source: "payments",
        payload: JsonSerializer.Serialize(webhook)), ct);
}
```

A background worker claims inbox rows, invokes handlers, and acks when successful:

```csharp
var ids = await inbox.ClaimAsync(ownerToken, leaseSeconds: 20, batchSize: 25, ct);
foreach (var id in ids)
{
    try
    {
        await ProcessWebhookAsync(id, ct);
        await inbox.AckAsync(ownerToken, new[] { id }, ct);
    }
    catch
    {
        await inbox.AbandonAsync(ownerToken, new[] { id }, ct);
    }
}
```

## 2) Exactly-once projection into read models

When projecting to a read store, commit the projection and the inbox ack in a single unit to avoid replay:

```csharp
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(ct);
await using var tx = await connection.BeginTransactionAsync(ct);

await ApplyProjectionAsync(inboxRecord.Payload, connection, (SqlTransaction)tx);
await inbox.AckAsync(ownerToken, new[] { inboxRecord.Id }, ct);

await tx.CommitAsync(ct);
```

If the process crashes before commit, the lease eventually expires and another worker retries safely.

## 3) Poison message quarantine

After several retries, move a problematic inbox row to failed while retaining the original payload for investigation:

```csharp
if (attempts >= 10)
{
    await inbox.FailAsync(ownerToken, new[] { id }, ct);
    await quarantinePublisher.PublishAsync(id, payload, ct);
    continue;
}
```

## 4) Tenant-aware inbox routing

Pair `InboxRouter` with dynamic discovery to process per-tenant inboxes without redeploying:

```csharp
services.AddSingleton<IInboxDatabaseDiscovery, GlobalDatabaseInboxDiscovery>();
services.AddDynamicMultiSqlInbox(
    selectionStrategy: new RoundRobinInboxSelectionStrategy(),
    refreshInterval: TimeSpan.FromMinutes(5));
```

- New tenant inboxes are picked up automatically.
- The dispatcher respects leases per tenant so slow consumers cannot block others.
- Use [Dynamic Inbox Configuration](dynamic-inbox-example.md) to implement `GlobalDatabaseInboxDiscovery`.

## 5) Coordinating inbox and outbox

For request/response flows, record the inbound message in inbox and emit a reply via outbox in the same transaction:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await inbox.AckAsync(ownerToken, new[] { inbound.Id }, ct); // completes inbox entry
await outbox.EnqueueAsync("responses.sent", responsePayload, (SqlTransaction)tx, correlationId: inbound.MessageId);
await tx.CommitAsync(ct);
```

This guarantees the response is sent once even if the handler restarts mid-flight.

## Related guides

- [Inbox Quick Start](inbox-quickstart.md)
- [Work Queue Implementation](work-queue-implementation.md)
- [Platform Primitives Overview](platform-primitives-overview.md)
- [Lease Examples](lease-examples.md)
