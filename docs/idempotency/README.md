# Idempotency

Bravellian.Platform.Idempotency is a provider-agnostic abstraction for tracking idempotency keys across retries and worker restarts.

## When to use it

Use `IIdempotencyStore` when you need to ensure a logical operation (email send, webhook call, billing charge, etc.) executes at-most-once per key even when work is retried.

## Core API

- `TryBeginAsync`: attempt to claim a key for processing.
- `CompleteAsync`: mark the key as finished.
- `FailAsync`: release the key for retry.

## SQL Server implementation

Register the SQL Server store and optional schema deployment:

```csharp
services.AddSqlIdempotency(
    connectionString: "Server=.;Database=app;Trusted_Connection=True;",
    schemaName: "infra",
    tableName: "Idempotency",
    lockDuration: TimeSpan.FromMinutes(5),
    enableSchemaDeployment: true);
```

## Notes

- Use stable, deterministic idempotency keys.
- Call `FailAsync` when the operation should be retried later.
- Use `CompleteAsync` only for terminal success or failure outcomes.
