# Lease Lock v2 Usage Guide

This document describes the new Lease Lock v2 functionality that provides DB-authoritative timestamps and monotonic scheduling for distributed lease management.

## Overview

The new lease system consists of:

- **`infra.Lease` table**: Stores lease information with DB-authoritative timestamps
- **`LeaseApi`**: Provides low-level data access operations
- **`LeaseRunner`**: High-level lease manager with automatic renewal using monotonic clock scheduling

## Key Features

1. **DB-Authoritative Timestamps**: The database decides lease validity using `SYSUTCDATETIME()`
2. **Monotonic Scheduling**: Uses `IMonotonicClock` for renewal timing, resilient to system clock changes and GC pauses
3. **Configurable Renewal**: Renews at configurable percentage of lease duration (default 60%)
4. **Jitter Prevention**: Adds random jitter to prevent herd behavior
5. **Server Time Synchronization**: Returns server time and next expiry to reduce round-trips

## Usage Examples

### Basic LeaseRunner Usage

```csharp
// Setup
var leaseApi = new LeaseApi(connectionString, "infra");
var monotonicClock = new MonotonicClock();
var timeProvider = TimeProvider.System;
var logger = serviceProvider.GetRequiredService<ILogger>();

// Ensure schema exists
await DatabaseSchemaManager.EnsureLeaseSchemaAsync(connectionString);

// Acquire a lease
var runner = await LeaseRunner.AcquireAsync(
    leaseApi,
    monotonicClock,
    timeProvider,
    leaseName: "my-critical-process",
    owner: Environment.MachineName,
    leaseDuration: TimeSpan.FromMinutes(5),
    renewPercent: 0.6, // Renew at 60% of lease duration
    logger: logger);

if (runner != null)
{
    try
    {
        // Do work while holding the lease
        await DoWork(runner.CancellationToken);
    }
    finally
    {
        await runner.DisposeAsync();
    }
}
```

### Advanced Usage with Manual Renewal

```csharp
var runner = await LeaseRunner.AcquireAsync(
    leaseApi, monotonicClock, timeProvider,
    "my-lease", "worker-1", TimeSpan.FromMinutes(2));

if (runner != null)
{
    try
    {
        while (!runner.CancellationToken.IsCancellationRequested)
        {
            // Check if lease is still valid
            runner.ThrowIfLost(); // Throws LostLeaseException if lost
            
            // Do some work
            await ProcessItem();
            
            // Manual renewal if needed
            var renewed = await runner.TryRenewNowAsync();
            if (!renewed)
            {
                logger.LogWarning("Failed to renew lease, stopping work");
                break;
            }
        }
    }
    catch (LostLeaseException ex)
    {
        logger.LogWarning("Lease lost: {Message}", ex.Message);
    }
    finally
    {
        await runner.DisposeAsync();
    }
}
```

### Low-Level LeaseApi Usage

```csharp
var leaseApi = new LeaseApi(connectionString, "infra");

// Acquire lease
var result = await leaseApi.AcquireAsync("my-lease", "owner-1", 300); // 5 minutes
if (result.Acquired)
{
    logger.LogInformation("Acquired lease until {Expiry} (server time: {ServerTime})", 
        result.LeaseUntilUtc, result.ServerUtcNow);
    
    // Renew lease
    var renewResult = await leaseApi.RenewAsync("my-lease", "owner-1", 300);
    if (renewResult.Renewed)
    {
        logger.LogInformation("Renewed lease until {Expiry}", renewResult.LeaseUntilUtc);
    }
}
```

## Configuration Recommendations

### Lease Duration Guidelines

- **Short-lived processes** (< 1 minute): 30-60 seconds
- **Medium processes** (1-10 minutes): 2-5 minutes  
- **Long-running processes** (> 10 minutes): 5-15 minutes

### Renewal Percentage

- **Default**: 0.6 (60%) - Good balance of safety and efficiency
- **Conservative**: 0.5 (50%) - More frequent renewals, higher safety margin
- **Aggressive**: 0.7-0.8 (70-80%) - Fewer renewals, requires stable environment

### Safety Margins

The system includes built-in safety features:

1. **Monotonic timing** prevents renewal issues during system clock adjustments
2. **Random jitter** (0-1 second) prevents thundering herd on renewal
3. **DB-authoritative expiry** ensures consistent lease state across all clients
4. **Automatic cancellation** when lease is lost

## Error Handling

```csharp
try
{
    var runner = await LeaseRunner.AcquireAsync(/* ... */);
    if (runner == null)
    {
        // Lease already held by another process
        logger.LogInformation("Could not acquire lease, another process is running");
        return;
    }
    
    // Use runner.CancellationToken for cooperative cancellation
    await DoWork(runner.CancellationToken);
}
catch (LostLeaseException ex)
{
    // Lease was lost during execution
    logger.LogWarning("Lease lost: {Message}", ex.Message);
}
catch (OperationCanceledException) when (runner?.CancellationToken.IsCancellationRequested == true)
{
    // Work was cancelled due to lease loss
    logger.LogInformation("Work cancelled due to lease loss");
}
```

## Migration from Legacy System

If migrating from the existing `SqlLease` system:

1. Update database schema: `await DatabaseSchemaManager.EnsureLeaseSchemaAsync(connectionString);`
2. Replace `SqlLease.AcquireAsync()` with `LeaseRunner.AcquireAsync()`
3. Use `runner.CancellationToken` instead of `lease.CancellationToken`
4. Replace `lease.ThrowIfLost()` with `runner.ThrowIfLost()`
5. Update error handling for the new `LostLeaseException` constructor

The new system is designed to be more resilient and provides better observability through server timestamp returns.
