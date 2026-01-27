# Email Outbox

Bravellian.Platform.Email provides a provider-agnostic outbox core for reliable email delivery.
It focuses on idempotent enqueueing, explicit dispatching, and provider adapters (like Postmark).

## Architecture

The system separates enqueue from processing for reliability and control:

- **Enqueue**: application code builds an `OutboundEmailMessage` and calls `IEmailOutbox.EnqueueAsync`.
  - The message is validated and serialized to the platform outbox (`IOutbox`).
  - `IEmailDeliverySink.RecordQueuedAsync` is called for observability.
- **Processing**: a worker calls `EmailOutboxProcessor.ProcessOnceAsync` (or the hosted service loop).
  - `IOutboxStore` claims due messages, `IOutboundEmailSender` sends them, and `IEmailDeliverySink` records attempts and final state.
  - `IIdempotencyStore` enforces message-key dedupe across workers.
  - `IEmailSendPolicy` can throttle, delay, or reject sends before the provider call.

## Quick Start

```csharp
var outbox = new EmailOutbox(platformOutbox, deliverySink);

var message = new OutboundEmailMessage(
    messageKey: "postmark:welcome:123",
    from: new EmailAddress("noreply@acme.com", "Acme"),
    to: new[] { new EmailAddress("user@acme.com") },
    subject: "Welcome",
    textBody: "Hello from the outbox!",
    htmlBody: null);

await outbox.EnqueueAsync(message, CancellationToken.None);

var processor = new EmailOutboxProcessor(
    outboxStore,
    outboundSender,
    idempotencyStore,
    deliverySink);

await processor.ProcessOnceAsync(CancellationToken.None);
```

## Idempotency and the Outbox Pattern

### MessageKey rules

`MessageKey` is the stable idempotency key for a logical message. It must:

- Be **stable across retries** and worker restarts.
- Be **unique per logical email send** (not per attempt).
- Be **deterministic** from your business context.

Good examples:

- `welcome:{userId}` for a once-per-user welcome email.
- `invoice:{invoiceId}:receipt` for a receipt tied to an invoice.
- `password-reset:{resetTokenId}` for a single reset flow.

Bad examples:

- A random GUID generated at enqueue time.
- A timestamp-based key (`welcome:2025-01-01T12:00:00Z`).

The processor uses `IIdempotencyStore` to ensure concurrent workers cannot double-send.
If a duplicate key is detected, the message is suppressed and recorded as `Suppressed`.

## Retry / Backoff Tuning

`EmailOutboxProcessorOptions` controls retries and backoff behavior:

- `MaxAttempts` caps attempts before a message is marked permanently failed.
- `BackoffPolicy` controls delay between retries (default exponential).

Example (custom backoff):

```csharp
services.AddBravellianEmailCore(
    configureProcessorOptions: options =>
    {
        options.MaxAttempts = 5;
        options.BackoffPolicy = attempt => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
    });
```

Notes:

- `EmailDeliveryStatus.FailedTransient` is retried until `MaxAttempts` is reached.
- All failures are capped even if they are transient to avoid infinite loops.

## Throttling Policies

Use `IEmailSendPolicy` to apply rate limits or scheduled delays before calling the provider.

### Global rate limit

```csharp
services.AddSingleton<IEmailSendPolicy>(
    new FixedRateLimitPolicy(limit: 100, window: TimeSpan.FromMinutes(1), perRecipient: false, TimeProvider.System));
```

### Per-recipient rate limit

```csharp
services.AddSingleton<IEmailSendPolicy>(
    new FixedRateLimitPolicy(limit: 5, window: TimeSpan.FromMinutes(10), perRecipient: true, TimeProvider.System));
```

Policy outcomes:

- **Allow**: message is sent immediately.
- **Delay**: message is rescheduled until `DelayUntilUtc`.
- **Reject**: message is marked `FailedPermanent` and recorded.

## Provider Adapters

### Core responsibilities (Bravellian.Platform.Email)

- Provider-agnostic message model and validation (`OutboundEmailMessage`, `EmailMessageValidator`).
- Outbox enqueue and processing (`IEmailOutbox`, `EmailOutboxProcessor`).
- Idempotency and delivery tracking interfaces (`IIdempotencyStore`, `IEmailDeliverySink`).
- Throttling policy (`IEmailSendPolicy`).

### Postmark adapter responsibilities (Bravellian.Platform.Email.Postmark)

- Translate `OutboundEmailMessage` into Postmark payloads.
- Inject provider headers/metadata (including `MessageKey`).
- Classify Postmark HTTP errors into transient vs permanent results.
- Provide webhook integration for bounce/suppression tracking.

### App-specific responsibilities

- Choose `MessageKey` and business-level dedupe rules.
- Configure retry/backoff and throttling.
- Implement `IEmailDeliverySink` to persist status updates.
- Register DI and hosted processing loop.

## Delivery Tracking via Postmark Webhooks

- Register `PostmarkWebhookProvider` to translate Postmark webhooks into provider-neutral delivery updates.
- Correlation is performed via `MessageKey` (sent in metadata/header) and/or provider message id.

```csharp
services.AddSingleton<IWebhookProvider>(sp =>
    new PostmarkWebhookProvider(
        sp.GetRequiredService<IEmailDeliverySink>(),
        new PostmarkWebhookOptions
        {
            SigningSecret = "postmark-signing-secret",
        }));
```

Use the Bravellian webhooks infrastructure to ingest and process events:

```csharp
services.AddBravellianWebhooks();

app.MapPost("/webhooks/{provider}", (HttpContext ctx, IWebhookIngestor ingestor) =>
    WebhookEndpoint.HandleAsync(ctx, ingestor));
```

## ASP.NET Core Helpers

- `Bravellian.Platform.Email.AspNetCore` supplies DI helpers for registering the outbox components.
- Hosting layers should wire up `IOutbox`/`IOutboxStore`, `IOutboundEmailSender`, and `IIdempotencyStore`.

Enqueue usage:

```csharp
public sealed class WelcomeEmails
{
    private readonly IEmailOutbox outbox;

    public WelcomeEmails(IEmailOutbox outbox)
    {
        this.outbox = outbox;
    }

    public Task SendAsync(string recipient, CancellationToken cancellationToken)
    {
        var message = new OutboundEmailMessage(
            messageKey: $"welcome:{recipient}",
            from: new EmailAddress("noreply@acme.com", "Acme"),
            to: new[] { new EmailAddress(recipient) },
            subject: "Welcome",
            textBody: "Hello!",
            htmlBody: null);

        return outbox.EnqueueAsync(message, cancellationToken);
    }
}
```

Wiring + running processor:

```csharp
services.AddBravellianEmailCore();
services.AddBravellianEmailPostmark(options =>
{
    options.ServerToken = "postmark-token";
    options.MessageStream = "transactional";
});

services.AddBravellianEmailProcessingHostedService(options =>
{
    options.PollInterval = TimeSpan.FromSeconds(5);
});
```

## Observability integration

Use `Bravellian.Platform.Observability` conventions to tie email sends and webhooks into audit + operations.

- Use `PlatformEventNames.EmailSent` and `PlatformEventNames.WebhookReceived` for audit events.
- Include `PlatformTagKeys.MessageKey`, `PlatformTagKeys.Provider`, and `PlatformTagKeys.WebhookEventId` in tags.
- If you are tracking long-running sends, emit operation events via `IPlatformEventEmitter`.

Example:

```csharp
await platformEventEmitter.EmitAuditEventAsync(
    new AuditEvent(
        AuditEventId.NewId(),
        DateTimeOffset.UtcNow,
        PlatformEventNames.EmailSent,
        "Transactional email sent",
        EventOutcome.Success,
        new[] { new EventAnchor("Email", message.MessageKey, "Subject") },
        correlation: correlationContext),
    cancellationToken);
```
## Troubleshooting

### Duplicate sends

- Ensure `MessageKey` is stable and deterministic for the logical email.
- Confirm `IIdempotencyStore` persists across workers and restarts.
- If duplicates persist, verify the outbox topic, correlation id, and policy behavior.

### Send barrages (too many emails)

- Add or tighten an `IEmailSendPolicy` rate limit.
- Consider per-recipient limits to prevent accidental bursts.
- Reduce `EmailOutboxProcessorOptions.BatchSize` if the provider has strict caps.

### Poison messages

- Messages marked `FailedPermanent` exceeded retries or had permanent errors.
- Inspect `IEmailDeliverySink` records to correlate provider errors and payloads.

### Correlation and tracking

- Use `MessageKey` in logs and persistence as the primary key.
- If the provider returns an id, record it in `IEmailDeliverySink` for lookup.
- Ensure Postmark metadata includes `MessageKey` to correlate webhook events.

