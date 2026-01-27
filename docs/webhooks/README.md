# Webhooks

Bravellian.Platform.Webhooks is a provider-agnostic webhook ingestion stack. The core library focuses on verification, classification, and delivery so providers can share one consistent pipeline. The ASP.NET Core package adds endpoint helpers and DI wiring without coupling the core to ASP.NET.

## Core concepts
- Fast-ack: return a 2xx response as quickly as possible to avoid provider retries and timeouts.
- Authenticate/verify: validate signatures, timestamps, and replay protection before doing any work.
- Classify: map the inbound payload to a webhook kind (event type, topic, or category) that drives routing.
- Enqueue: hand off validated work to a durable queue or background runner.
- Process: execute handlers with retries, idempotency, and observability.

## Architecture overview

### Ingest path (request-time)
1) Endpoint captures the raw request into a `WebhookEnvelope`.
2) Provider authenticator validates signature/timestamp.
3) Provider classifier extracts metadata and decides Accepted / Ignored / Rejected.
4) Accepted events are written to the inbox with a dedupe key.
5) A fast 2xx (typically 202) is returned to the provider.

### Processing path (background)
1) Processor claims pending inbox items.
2) Payload is deserialized into `WebhookEventRecord`.
3) Provider handlers are selected by event type and invoked.
4) Success => Completed. Failures => retry with backoff; too many attempts => Poisoned.

## Dedupe strategy
- Preferred: provider event id (`ProviderEventId`) for stable idempotency.
- Fallback: SHA-256 of request body.
- The body-hash dedupe is **weak** (different payloads can collide across providers if upstream changes formatting or timestamps); use provider event ids whenever possible.

## Rejected events storage policy
- Default: rejected events are **not** stored.
- Optional: enable `WebhookOptions.StoreRejected = true` to persist rejections for auditing.
- Optional redaction: `WebhookOptions.RedactRejectedBody = true` stores headers and metadata only (body cleared).

## Handler guidance
- Handlers **must be idempotent**. Processing is at-least-once.
- Expect replays and retries; guard on `DedupeKey` or your own business idempotency.
- Keep handlers small and side-effect aware; prefer writing domain events to an outbox.
- Common status flow: `Pending` → `Processing` → `Completed` or `FailedRetryable` → `Poisoned`.

## Operations
- **Poison handling**: inspect poisoned items, fix underlying cause, then replay.
- **Replay strategy**: re-enqueue with the original dedupe key or a scoped replay key.
- **Backoff tuning**: `WebhookProcessorOptions.BaseBackoff`, `MaxBackoff`, `MaxAttempts` should align with provider retry windows and your processing SLOs.

## Security notes
- Always verify the **raw** request body; do not parse or normalize before signature validation.
- Validate timestamps and enforce max age to prevent replay attacks.
- Reject missing/invalid signatures with 401/403.
- Use constant-time comparisons for signatures where possible.

## Versioning / compatibility notes
- The core library is provider-agnostic and avoids ASP.NET dependencies.
- Public contracts are additive; breaking changes will be noted in release notes.
- Wire format (envelope/record JSON) should be treated as stable for inbox compatibility.

## Observability
- Use `WebhookTelemetryEvents` for consistent event names across logs and metrics.
- Configure callbacks in `WebhookOptions` to emit telemetry without coupling core logic to a logging framework.
- The ASP.NET Core integration wires default logging via `ILogger` when you call `AddBravellianWebhooks`.

```csharp
builder.Services.AddBravellianWebhooks(options =>
{
    options.OnIngested = (result, envelope) =>
    {
        // Emit custom metrics or traces.
    };

    options.OnProcessed = (result, context) =>
    {
        // Track processing outcomes.
    };

    options.OnRejected = (reason, envelope, result) =>
    {
        // Capture rejection details.
    };
});
```

## Quick start examples

### 1) Provider implementation (example)
```csharp
public sealed class AcmeWebhookProvider : WebhookProviderBase
{
    public AcmeWebhookProvider()
        : base(new AcmeAuthenticator(), new AcmeClassifier(), new IWebhookHandler[] { new InvoicePaidHandler() })
    {
    }

    public override string Name => "acme";
}

public sealed class AcmeAuthenticator : IWebhookAuthenticator
{
    public Task<AuthResult> AuthenticateAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Headers.TryGetValue("X-Signature", out var signature)
            && signature == "ok")
        {
            return Task.FromResult(new AuthResult(true, null));
        }

        return Task.FromResult(new AuthResult(false, "Invalid signature."));
    }
}

public sealed class AcmeClassifier : IWebhookClassifier
{
    public Task<ClassifyResult> ClassifyAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(envelope.BodyBytes);
        var root = doc.RootElement;
        var eventId = root.GetProperty("eventId").GetString();
        var eventType = root.GetProperty("eventType").GetString();
        var dedupe = WebhookDedupe.Create(envelope.Provider, eventId, envelope.BodyBytes).Key;

        return Task.FromResult(new ClassifyResult(
            WebhookIngestDecision.Accepted,
            eventId,
            eventType,
            dedupe,
            null,
            null,
            null));
    }
}

public sealed class InvoicePaidHandler : IWebhookHandler
{
    public bool CanHandle(string eventType) => eventType == "invoice.paid";

    public Task HandleAsync(WebhookEventContext context, CancellationToken ct)
    {
        // Parse context.BodyBytes and execute domain logic.
        return Task.CompletedTask;
    }
}
```

### 2) Provider registration (example)
```csharp
builder.Services.AddSingleton<IWebhookProvider, AcmeWebhookProvider>();
builder.Services.AddBravellianWebhooks();
```

### 3) ASP.NET Core minimal API mapping
```csharp
app.MapPost("/webhooks/{provider}", async (HttpContext context, string provider, IWebhookIngestor ingestor, CancellationToken ct) =>
{
    return await WebhookEndpoint.HandleAsync(context, provider, ingestor, ct);
});
```

The endpoint returns 202 Accepted for successful ingestion, and 401/403 for rejected requests.

### 4) Running the processor from a hosted service (snippet)
```csharp
builder.Services.Configure<WebhookProcessingOptions>(options =>
{
    options.PollInterval = TimeSpan.FromSeconds(1);
    options.BatchSize = 100;
    options.MaxAttempts = 5;
});

builder.Services.AddBravellianWebhooks()
    .AddWebhookProcessingHostedService();
```

### 5) Core pipeline (illustrative)
```csharp
// Pseudo-code illustrating the core flow.
var result = await ingestor.IngestAsync("stripe", envelope, ct);
if (result.Decision == WebhookIngestDecision.Rejected)
{
    return Results.StatusCode((int)result.HttpStatusCode);
}

// Accepted or ignored responses should return 202/200 immediately.
```
