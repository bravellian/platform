# Distributed Semaphores

## Overview

Distributed semaphores provide cluster-wide concurrency control for multi-node applications. They allow you to limit the number of concurrent operations across all instances of your application, preventing resource exhaustion and maintaining system stability.

## When to Use Semaphores

Use distributed semaphores when you need to:

- **Limit concurrent external API calls** across all nodes to stay within rate limits
- **Control database load** by limiting concurrent expensive queries
- **Manage resource consumption** (CPU, memory, connections) across a cluster
- **Coordinate work distribution** when processing shared queues or events

## Availability

Distributed semaphores are available when using:

- `AddPlatformMultiDatabaseWithControlPlaneAndList` (uses control plane database)
- `AddPlatformMultiDatabaseWithControlPlaneAndDiscovery` (uses control plane database)

They are **not available** in:
- Multi-database without control plane (`AddPlatformMultiDatabaseWithList`, `AddPlatformMultiDatabaseWithDiscovery`)

**Note**: For single database scenarios, use `AddPlatformMultiDatabaseWithControlPlaneAndList` with the same connection string for both the application database and control plane. Semaphores use the control plane database.

## Core Concepts

### Semaphore Name

A unique identifier for a semaphore. Names are:
- Maximum 200 characters
- Allowed characters: letters, digits, `-`, `_`, `:`, `/`, `.`
- Example: `external-api:stripe/payment-create`

### Limit

The maximum number of concurrent lease holders for a semaphore. Must be between 1 and the configured maximum (default 10,000).

### Lease

A time-limited grant to perform an operation. Each lease has:
- **Token**: Unique identifier for this specific lease
- **Fencing Counter**: Strictly monotonic integer per semaphore (for ordering)
- **Owner ID**: Stable identifier for the holder (e.g., `{hostname}-{pid}-{guid}`)
- **Expiry**: UTC timestamp when the lease expires

### Time-to-Live (TTL)

How long a lease remains valid. Must be between configured bounds (default 1-3600 seconds).

## Basic Usage

```csharp
// Inject the service
private readonly ISemaphoreService semaphoreService;

public MyService(ISemaphoreService semaphoreService)
{
    this.semaphoreService = semaphoreService;
}

// Create a semaphore with limit 10
await semaphoreService.EnsureExistsAsync("my-semaphore", limit: 10);

// Try to acquire a lease
var result = await semaphoreService.TryAcquireAsync(
    name: "my-semaphore",
    ttlSeconds: 30,
    ownerId: "worker-1");

if (result.Status == SemaphoreAcquireStatus.Acquired)
{
    try
    {
        // Perform protected work
        await DoExpensiveOperationAsync();
    }
    finally
    {
        // Always release when done
        await semaphoreService.ReleaseAsync("my-semaphore", result.Token!.Value);
    }
}
else
{
    // Could not acquire - at capacity
    // Implement backoff/retry logic as needed
}
```

## Advanced Patterns

### Lease Renewal

For long-running operations, renew the lease periodically:

```csharp
var result = await semaphoreService.TryAcquireAsync(
    name: "my-semaphore",
    ttlSeconds: 30,
    ownerId: "worker-1");

if (result.Status == SemaphoreAcquireStatus.Acquired)
{
    using var cts = new CancellationTokenSource();
    
    // Renew every 10 seconds (1/3 of TTL for safety margin)
    var renewTask = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            var renewResult = await semaphoreService.RenewAsync(
                "my-semaphore",
                result.Token!.Value,
                ttlSeconds: 30);
            
            if (renewResult.Status != SemaphoreRenewStatus.Renewed)
            {
                // Lease was lost - stop work
                break;
            }
        }
    });

    try
    {
        await DoLongRunningOperationAsync(cts.Token);
    }
    finally
    {
        cts.Cancel();
        await renewTask;
        await semaphoreService.ReleaseAsync("my-semaphore", result.Token.Value);
    }
}
```

### Idempotent Acquire with Client Request ID

For retry scenarios, use a client request ID to avoid creating duplicate leases:

```csharp
var clientRequestId = Guid.NewGuid().ToString();

// First attempt
var result = await semaphoreService.TryAcquireAsync(
    name: "my-semaphore",
    ttlSeconds: 30,
    ownerId: "worker-1",
    clientRequestId: clientRequestId);

// On retry (e.g., after network error), same clientRequestId
// returns the existing lease if still valid
var retryResult = await semaphoreService.TryAcquireAsync(
    name: "my-semaphore",
    ttlSeconds: 30,
    ownerId: "worker-1",
    clientRequestId: clientRequestId);

// retryResult.Token == result.Token if original lease still active
```

### Fencing Tokens

Use fencing counters to prevent stale operations:

```csharp
var result = await semaphoreService.TryAcquireAsync(
    name: "my-semaphore",
    ttlSeconds: 30,
    ownerId: "worker-1");

if (result.Status == SemaphoreAcquireStatus.Acquired)
{
    // Include fencing in your state updates
    await UpdateStateAsync(
        data: myData,
        fencing: result.Fencing!.Value);
}

// In your state update logic:
async Task UpdateStateAsync(MyData data, long fencing)
{
    // Only accept updates with higher fencing counters
    var currentFencing = await GetCurrentFencingAsync();
    if (fencing <= currentFencing)
    {
        throw new InvalidOperationException("Stale fencing token");
    }
    
    // Proceed with update
}
```

## Operational Considerations

### Choosing TTL

- **Short TTL (1-10s)**: Quick recovery from crashes, higher renewal overhead
- **Medium TTL (30-60s)**: Balanced - good for most use cases
- **Long TTL (5-60min)**: Reduces renewal overhead but slower recovery

**Rule of thumb**: Set TTL to 3x your typical operation duration, with a minimum of 30 seconds.

### Choosing Limits

Start conservative:
1. **Measure**: Observe current concurrent operation count
2. **Set Limit**: Start at 2x observed maximum
3. **Monitor**: Track `NotAcquired` rates
4. **Adjust**: Increase if too many rejections, decrease if resources strained

### Monitoring

Watch for:
- **High `NotAcquired` rate**: Increase limit or add more capacity
- **Many expired leases**: Workers may be crashing or TTL too short
- **Low acquisition success**: Consider backoff/retry strategies

### Starvation and backpressure

- Repeated `NotAcquired` responses act as **built-in backpressure**, not dead-lettering. Requests are rejected while the limit is saturated and immediately succeed once capacity returns.
- If callers are perennially starved, tune **limit** and **TTL** together: longer TTL reduces renewal chatter but keeps capacity occupied longer; shorter TTL speeds recovery but increases renew load.
- Ensure the **reaper cadence** (`ReaperCadenceSeconds`/`ReaperBatchSize`) is sized to clear leaked leases quickly enough for your workload.
- Track `NotAcquired` and renewal failures in metrics; use exponential backoff to avoid thundering herds during contention.

### Limit Changes

You can change limits dynamically:

```csharp
// Increase limit
await semaphoreService.UpdateLimitAsync("my-semaphore", newLimit: 20);

// Decrease limit (won't force-revoke active leases)
await semaphoreService.UpdateLimitAsync("my-semaphore", newLimit: 5);
```

**Note**: Decreasing limits doesn't revoke existing leases. New acquires are blocked until active count falls below the new limit through releases or expiries.

## Configuration

Configure via `SemaphoreOptions`:

```csharp
services.Configure<SemaphoreOptions>(options =>
{
    options.MinTtlSeconds = 1;           // Minimum TTL allowed
    options.MaxTtlSeconds = 3600;        // Maximum TTL allowed (1 hour)
    options.DefaultTtlSeconds = 30;      // Default TTL for helpers
    options.MaxLimit = 10000;            // Maximum limit per semaphore
    options.ReaperCadenceSeconds = 30;   // Reaper runs every 30s
    options.ReaperBatchSize = 1000;      // Max rows deleted per iteration
});
```

## Best Practices

1. **Always release in finally blocks** to prevent leaked leases
2. **Implement retry with exponential backoff** when acquire returns `NotAcquired`
3. **Use descriptive names** that indicate purpose: `external-api:provider/operation`
4. **Renew at 1/3 TTL intervals** for safety margin
5. **Monitor fencing counters** if order matters
6. **Test failure scenarios** (crashes, network issues, slow operations)
7. **Start with conservative limits** and adjust based on monitoring

## Troubleshooting

### "Semaphore not found" errors

Create the semaphore first:
```csharp
await semaphoreService.EnsureExistsAsync("my-semaphore", limit: 10);
```

### Frequent "NotAcquired" responses

- Check current limit: is it too low?
- Monitor active lease count
- Consider increasing limit or adding more resources

### Leases expiring during work

- Increase TTL
- Add renewal logic for long operations
- Check if workers are actually finishing work

### Background reaper not running

Ensure you're using a control-plane registration mode. The `SemaphoreReaperService` is automatically registered as a hosted service.

## Safety Guarantees

✅ **Never exceed limit**: Active leases for a semaphore never exceed its configured limit
✅ **Monotonic fencing**: Fencing counters strictly increase per semaphore name
✅ **DB UTC time**: All time comparisons use database UTC, tolerating clock skew
✅ **Idempotent operations**: Renew/Release safe under retries
✅ **Automatic cleanup**: Background reaper removes expired leases
