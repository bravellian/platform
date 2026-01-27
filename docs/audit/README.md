# Audit

## Purpose

`Bravellian.Platform.Audit` provides provider-agnostic primitives for an immutable, human-readable audit timeline. Events are append-only and queryable by anchors so UIs and operators can reconstruct what happened without relying on provider-specific schemas.

## Concepts

- **AuditEventId**: stable identifier for a single audit event.
- **AuditEvent**: immutable event record (name, message, outcome, anchors, actor, correlation).
- **EventAnchor**: a queryable key that ties events to domain entities.
- **AuditActor**: actor details (type, id, display).
- **EventOutcome**: `Success`, `Failure`, `Warning`, `Info`.
- **AuditQuery**: minimal query filters (anchors, time range, name, limit).
- **IAuditEventWriter**: append-only event writer contract.
- **IAuditEventReader**: query contract for reading timelines.
- **AuditEventValidator**: validates required fields and size limits for `DataJson`.

## Quickstart

```csharp
var auditEvent = new AuditEvent(
    AuditEventId.NewId(),
    DateTimeOffset.UtcNow,
    name: "invoice.created",
    displayMessage: "Invoice INV-1001 created",
    outcome: EventOutcome.Success,
    anchors: new[]
    {
        new EventAnchor("Invoice", "INV-1001", "Subject"),
        new EventAnchor("Tenant", "TEN-01", "Owner"),
    },
    actor: new AuditActor("System", "billing-service", "Billing Service"));

await writer.WriteAsync(auditEvent, cancellationToken);
```

## End-to-end examples

### Write audit events for email send attempts

```csharp
var auditEvent = new AuditEvent(
    AuditEventId.NewId(),
    DateTimeOffset.UtcNow,
    name: "email.send.attempt",
    displayMessage: "Email send attempted for message key m-2849",
    outcome: EventOutcome.Info,
    anchors: new[]
    {
        new EventAnchor("Email", "m-2849", "Subject"),
        new EventAnchor("Tenant", "TEN-01", "Owner"),
    },
    dataJson: "{\"provider\":\"postmark\",\"attempt\":1}",
    actor: new AuditActor("System", "email-outbox", "Email Outbox"),
    correlation: correlationContext);

await writer.WriteAsync(auditEvent, cancellationToken);
```

### Query a timeline by anchor

```csharp
var query = new AuditQuery(
    anchors: new[] { new EventAnchor("Invoice", "INV-1001", "Subject") },
    fromUtc: DateTimeOffset.UtcNow.AddDays(-30),
    name: "invoice.*",
    limit: 50);

var events = await reader.QueryAsync(query, cancellationToken);
```

## Guidance

### What belongs in platform vs app-specific naming/UI

**Platform (this library)**
- DTOs, interfaces, and validation.
- Correlation-aware event shapes.

**App**
- Event taxonomy and display message style.
- Storage implementation and indexing strategies.
- UI rendering and permissions.

### Naming conventions for audit event names

- Use stable, dot-separated, lowercase names: `invoice.created`, `email.send.attempt`, `webhook.processed`.
- Do not embed user-provided values in the name; use anchors or `DataJson` instead.
- Keep names consistent across services and providers.

### Retention strategies (platform-agnostic)

- Keep recent events hot (e.g., 30-90 days), archive older events.
- Retain only what is required for compliance and operational needs.
- Store large payloads externally and reference them by id in `DataJson`.

## Operational notes

### Query patterns for timelines

- Always query by anchors; add time range and name filters for performance.
- Use correlation to stitch together inbox/outbox, operations, and audit timelines.

### Avoiding PII in audit data

- Do not store raw request bodies or secrets in `DisplayMessage` or `DataJson`.
- Prefer stable ids and hashed values.
- Keep `DataJson` small; validate size using `AuditEventValidator` options.

## Observability glue

Use `Bravellian.Platform.Observability` to emit standardized audit events for inbox/outbox, webhooks, and email sends.