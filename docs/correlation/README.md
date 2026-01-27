# Correlation

## Purpose

`Bravellian.Platform.Correlation` supplies consistent correlation identifiers across UI actions, inbox/outbox processing, webhooks, email, and operations. It helps link distributed work into a single logical trace without prescribing storage or transport.

## Concepts

- **CorrelationId**: stable identifier for a logical flow.
- **CorrelationContext**: correlation, causation, trace/span, and tags for a single flow.
- **CorrelationHeaders**: standard header keys (`X-Correlation-Id`, `X-Causation-Id`).
- **ICorrelationIdGenerator**: creates new correlation ids.
- **ICorrelationContextAccessor**: access the ambient context (e.g., per request).
- **ICorrelationSerializer**: serialize/deserialize context to header dictionaries.
- **CorrelationScope**: `IDisposable` helper for setting ambient context in a scope.

## Quickstart

```csharp
var generator = new DefaultCorrelationIdGenerator();
var accessor = new AmbientCorrelationContextAccessor();

var correlationId = generator.NewId();
var context = new CorrelationContext(
    correlationId,
    causationId: null,
    traceId: null,
    spanId: null,
    createdAtUtc: DateTimeOffset.UtcNow);

using var scope = new CorrelationScope(accessor, context);
```

## End-to-end example

### Propagate correlation from HTTP header to outbox to operation/audit

```csharp
public sealed class EmailController
{
    private readonly ICorrelationSerializer serializer;
    private readonly ICorrelationContextAccessor accessor;
    private readonly IEmailOutbox outbox;
    private readonly IOperationTracker operations;
    private readonly IAuditEventWriter auditWriter;

    public EmailController(
        ICorrelationSerializer serializer,
        ICorrelationContextAccessor accessor,
        IEmailOutbox outbox,
        IOperationTracker operations,
        IAuditEventWriter auditWriter)
    {
        this.serializer = serializer;
        this.accessor = accessor;
        this.outbox = outbox;
        this.operations = operations;
        this.auditWriter = auditWriter;
    }

    public async Task<IActionResult> SendAsync(HttpRequest request, OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => (string?)h.Value);
        var correlation = serializer.Read(headers) ?? new CorrelationContext(
            CorrelationId.NewId(),
            causationId: null,
            traceId: null,
            spanId: null,
            createdAtUtc: DateTimeOffset.UtcNow);

        using var scope = new CorrelationScope(accessor, correlation);

        await outbox.EnqueueAsync(message, cancellationToken);

        var operationId = await operations.StartAsync(
            "Email.Send",
            correlation,
            parentOperationId: null,
            tags: new Dictionary<string, string> { ["messageKey"] = message.MessageKey },
            cancellationToken);

        await auditWriter.WriteAsync(
            new AuditEvent(
                AuditEventId.NewId(),
                DateTimeOffset.UtcNow,
                "email.send.enqueued",
                "Email send enqueued",
                EventOutcome.Info,
                new[] { new EventAnchor("Email", message.MessageKey, "Subject") },
                correlation: correlation),
            cancellationToken);

        return new AcceptedResult();
    }
}
```

## Guidance

### What belongs in platform vs app

**Platform (this library)**
- DTOs, headers, accessors, and serialization.
- Default id generation and scope helpers.

**App**
- Decisions on when to create new correlations vs reuse.
- Mapping correlation ids to tenants or domain entities.
- Logging, tracing, and UI representation.

### Naming and propagation rules

- Create a new `CorrelationId` at the boundary (UI action, external webhook).
- Use `CausationId` to link child actions to parent flows.
- Propagate correlation through headers, message metadata, and outbox payloads.

## Operational notes

- Do not put PII in correlation ids; treat them as opaque.
- Always include correlation in audit events and operations when available.
- Favor `CorrelationScope` to ensure ambient context flows across async calls.

## Observability glue

`Bravellian.Platform.Observability` uses correlation to tie together audit events, operations, and outbox processing.