# Exactly Once

Bravellian.Platform.ExactlyOnce provides a small, opt-in workflow for "exactly once" (best-effort) execution.
It composes idempotency with caller-defined execution and optional verification probes.

## Typical flow

1. Resolve a stable key for the logical operation.
2. Call `TryBeginAsync` via the executor.
3. Run the operation.
4. Mark complete or failed based on the outcome.

## Usage

```csharp
var executor = new ExactlyOnceExecutor<MyItem>(idempotencyStore, keyResolver);
var result = await executor.ExecuteAsync(
    item,
    async (payload, ct) =>
    {
        await handler(payload, ct);
        return ExactlyOnceExecutionResult.Success();
    },
    cancellationToken);

if (result.Outcome == ExactlyOnceOutcome.Retry)
{
    // reschedule work
}
```

## Notes

- The executor provides at-least-once transport with idempotent suppression.
- Optional probes can confirm whether an external side effect already occurred.
- Integration helpers for Inbox/Outbox live in Bravellian.Platform.
```
