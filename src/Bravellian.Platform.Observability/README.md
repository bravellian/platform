# Bravellian.Platform.Observability

Shared observability primitives for platform subsystems.

## Purpose

`Bravellian.Platform.Observability` supplies conventions and lightweight helpers that connect:

- correlation contexts
- operation tracking
- audit events
- inbox/outbox metadata

It keeps the surface area small while making observability data consistent across apps.

## Conventions

Standard event names are defined in `PlatformEventNames`:

- `outbox.message.processed`
- `webhook.received`
- `email.sent`
- `operation.started`
- `operation.completed`
- `operation.failed`

Standard tag keys are defined in `PlatformTagKeys`:

- `tenant`
- `partition`
- `provider`
- `messageKey`
- `operationId`
- `outboxMessageId`
- `inboxMessageId`
- `webhookEventId`

## Usage

Emit coordinated operation + audit events:

```csharp
var emitter = new PlatformEventEmitter(auditWriter, operationTracker, correlationAccessor);

var operationId = await emitter.EmitOperationStartedAsync(
    "NightlyImport",
    correlationContext: null,
    parentOperationId: null,
    tags: new Dictionary<string, string>
    {
        [PlatformTagKeys.Tenant] = "tenant-1",
    },
    cancellationToken);

await emitter.EmitOperationCompletedAsync(
    operationId,
    OperationStatus.Succeeded,
    "Completed",
    correlationContext: null,
    tags: null,
    cancellationToken);
```

For webhooks and email, use `PlatformTagKeys` to capture provider, message keys, and webhook IDs
and emit audit events with `PlatformEventNames`.