# Operations

## Purpose

`Bravellian.Platform.Operations` provides provider-agnostic primitives for tracking short-lived system jobs and long-running operations. It focuses on immutable identifiers, status transitions, and correlation-friendly metadata without prescribing a storage schema.

## Concepts

- **OperationId**: stable identifier for a single operation.
- **OperationSnapshot**: current operation state (status, timestamps, progress, correlation, metadata).
- **OperationEvent**: append-only evidence record (checkpoint, warning, error).
- **IOperationTracker**: contract for starting, updating, and completing operations.
- **IOperationWatcher**: contract for stalled detection.

## Quickstart

```csharp
// application composition
services.AddSingleton<IOperationTracker, MyOperationTracker>();

// usage
var operationId = await tracker.StartAsync(
    name: "NightlyImport",
    correlationContext: new CorrelationContext(new CorrelationId("corr-123"), null, null, null, DateTimeOffset.UtcNow),
    parentOperationId: null,
    tags: null,
    cancellationToken);

await tracker.UpdateProgressAsync(operationId, 50, "Halfway", cancellationToken);
await tracker.CompleteAsync(operationId, OperationStatus.Succeeded, "Done", cancellationToken);
```

## End-to-end examples

### Create an operation for a webhook processing run

```csharp
public sealed class WebhookHandler
{
    private readonly IOperationTracker tracker;
    private readonly IAuditEventWriter auditWriter;

    public WebhookHandler(IOperationTracker tracker, IAuditEventWriter auditWriter)
    {
        this.tracker = tracker;
        this.auditWriter = auditWriter;
    }

    public async Task HandleAsync(WebhookEnvelope envelope, CorrelationContext correlation, CancellationToken cancellationToken)
    {
        var operationId = await tracker.StartAsync(
            name: "Webhook.Process",
            correlationContext: correlation,
            parentOperationId: null,
            tags: new Dictionary<string, string>
            {
                ["provider"] = envelope.Provider,
                ["webhookEventId"] = envelope.EventId,
            },
            cancellationToken);

        try
        {
            await tracker.AddEventAsync(operationId, "Started", "Webhook received", envelope.PayloadJson, cancellationToken);
            // process webhook
            await tracker.CompleteAsync(operationId, OperationStatus.Succeeded, "Processed", cancellationToken);

            await auditWriter.WriteAsync(
                new AuditEvent(
                    AuditEventId.NewId(),
                    DateTimeOffset.UtcNow,
                    "webhook.processed",
                    "Webhook processed successfully",
                    EventOutcome.Success,
                    new[] { new EventAnchor("Webhook", envelope.EventId, "Subject") },
                    correlation: correlation),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await tracker.AddEventAsync(operationId, "Error", ex.Message, ex.ToString(), cancellationToken);
            await tracker.CompleteAsync(operationId, OperationStatus.Failed, ex.Message, cancellationToken);
            throw;
        }
    }
}
```

## Guidance

### What belongs in platform vs app-specific naming/UI

**Platform (this library)**
- DTOs, interfaces, and helpers for tracking operations.
- Status and progress semantics.

**App**
- Operation names (business meaning and UI strings).
- Tag taxonomy (tenant/partition, provider, message keys).
- Presentation and aggregation logic for dashboards.

### Naming conventions for operations

- Use stable, dot-separated names: `Webhook.Process`, `Email.Send`, `Invoice.Generate`.
- Avoid user-provided strings in operation names.
- Include domain scope if needed: `Billing.Invoice.Generate`.

### Retention strategies (platform-agnostic)

- Keep snapshots for active + recently completed operations (e.g., 7-30 days).
- Retain operation events longer if they serve as evidence.
- Archive summaries to cold storage if required for compliance.

## Operational notes

### Handling stalled operations

- Use `IOperationWatcher.FindStalledAsync` to identify operations with no recent updates.
- Set a threshold per operation type (e.g., 30 minutes for webhooks, hours for batch jobs).
- Mark stalled operations explicitly to unblock dashboards and alerting.

### Query patterns for timelines

- Load the `OperationSnapshot` for current state.
- Query `OperationEvent` records ordered by `OccurredAtUtc` for evidence.
- Join audit events by correlation id to show cross-system timelines.

### Avoiding PII

- Never store raw request bodies or sensitive identifiers in `Message`.
- Use hashed or tokenized values in `DataJson` when needed.
- Prefer stable domain IDs that are already safe to expose.

## Observability glue

Use `Bravellian.Platform.Observability` to emit coordinated operation + audit events.
`PlatformEventNames` includes `operation.started`, `operation.completed`, and `operation.failed` with standard tags.