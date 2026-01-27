# Idempotency

Bravellian.Platform.Idempotency provides a small abstraction for tracking idempotency keys across retries and workers.

## Core abstraction

- `IIdempotencyStore` manages the lifecycle of an idempotency key.
- `IIdempotencyStoreProvider` and `IIdempotencyStoreRouter` help resolve stores in multi-database setups.

## Typical flow

1. Call `TryBeginAsync` with a stable idempotency key.
2. If it returns `true`, perform the work.
3. Call `CompleteAsync` when the operation is finished.
4. Call `FailAsync` when the operation should be retried later.

## Usage

```csharp
public sealed class ChargeProcessor
{
    private readonly IIdempotencyStore store;

    public ChargeProcessor(IIdempotencyStore store)
    {
        this.store = store;
    }

    public async Task<bool> ProcessAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!await store.TryBeginAsync(idempotencyKey, cancellationToken))
        {
            return false;
        }

        try
        {
            // perform external call
            await store.CompleteAsync(idempotencyKey, cancellationToken);
            return true;
        }
        catch
        {
            await store.FailAsync(idempotencyKey, cancellationToken);
            throw;
        }
    }
}
```
