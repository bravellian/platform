# Time Abstractions in Bravellian Platform

This document explains the time abstraction system introduced to standardize time usage across the platform and improve testability.

## Overview

The platform uses two complementary time abstractions:

1. **`TimeProvider`** - For wall-clock timestamps and authoritative time values
2. **`IMonotonicClock`** - For duration measurements and timing that must be stable

## When to Use TimeProvider

Use `TimeProvider.GetUtcNow()` for:

- **Timestamps and dating** - When you need to record when something happened
- **Business logic timing** - Scheduling, due dates, creation/modification times
- **External communication** - API responses, log entries, audit trails
- **Database records** - CreatedAt, UpdatedAt, ScheduledTime fields

### Example:
```csharp
public class OrderService
{
    private readonly TimeProvider timeProvider;

    public OrderService(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CreatedAt = this.timeProvider.GetUtcNow(), // Wall clock timestamp
            Status = OrderStatus.Pending
        };
        
        // ... save to database
        return order;
    }
}
```

## When to Use IMonotonicClock

Use `IMonotonicClock` for:

- **Timeouts and deadlines** - "This operation must complete within 30 seconds"
- **Performance measurements** - Timing how long operations take
- **Rate limiting** - "Allow 10 requests per second"
- **Lease renewals** - Periodic background tasks
- **Retry delays** - Exponential backoff calculations

### Example:
```csharp
public class ApiClient
{
    private readonly IMonotonicClock clock;

    public ApiClient(IMonotonicClock clock)
    {
        this.clock = clock;
    }

    public async Task<T> CallWithTimeoutAsync<T>(Func<Task<T>> operation, TimeSpan timeout)
    {
        var deadline = MonoDeadline.In(this.clock, timeout);
        
        while (!deadline.Expired(this.clock))
        {
            try
            {
                return await operation();
            }
            catch (TransientException)
            {
                // Check if we still have time to retry
                if (deadline.Expired(this.clock))
                    throw new TimeoutException();
                    
                await Task.Delay(100); // Brief delay before retry
            }
        }
        
        throw new TimeoutException();
    }
}
```

## Key Differences

| Aspect | TimeProvider | IMonotonicClock |
|--------|-------------|-----------------|
| **Purpose** | Wall clock timestamps | Duration/timing measurements |
| **Affected by** | System clock changes, NTP adjustments | Nothing (stable) |
| **Use for** | "When did this happen?" | "How long until timeout?" |
| **Test with** | `FakeTimeProvider` | `FakeMonotonicClock` |

## Testing with Fake Implementations

### Testing TimeProvider Logic

```csharp
[Fact]
public async Task OrderService_SetsCorrectCreationTime()
{
    // Arrange
    var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T10:00:00Z"));
    var service = new OrderService(fakeTime);
    
    // Act
    var order = await service.CreateOrderAsync(new CreateOrderRequest());
    
    // Assert
    order.CreatedAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T10:00:00Z"));
}
```

### Testing Monotonic Clock Logic

```csharp
[Fact]
public async Task ApiClient_RespectsTimeout()
{
    // Arrange
    var fakeClock = new FakeMonotonicClock();
    var client = new ApiClient(fakeClock);
    var neverCompletes = () => Task.Delay(Timeout.Infinite);
    
    // Act & Assert
    var task = client.CallWithTimeoutAsync(neverCompletes, TimeSpan.FromSeconds(5));
    
    // Simulate 6 seconds passing
    fakeClock.Advance(TimeSpan.FromSeconds(6));
    
    await Should.ThrowAsync<TimeoutException>(() => task);
}
```

## Dependency Injection Setup

The time abstractions are automatically registered when you use any of the platform's service registration methods:

```csharp
services.AddSqlScheduler(options);
services.AddSqlOutbox(options);
services.AddSystemLeases(options);
```

Or register them manually:

```csharp
services.AddTimeAbstractions(); // Registers TimeProvider.System and IMonotonicClock
```

For testing, provide custom implementations:

```csharp
services.AddTimeAbstractions(new FakeTimeProvider(fixedTime));
services.AddSingleton<IMonotonicClock>(new FakeMonotonicClock());
```

## Migration from Direct Time Usage

### Before (Problematic)
```csharp
// DON'T: Direct DateTime usage
var timestamp = DateTime.UtcNow;

// DON'T: Wall clock for durations
var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
if (DateTimeOffset.UtcNow > deadline) // Can break with clock adjustments!
```

### After (Correct)
```csharp
// DO: Use TimeProvider for timestamps
var timestamp = timeProvider.GetUtcNow();

// DO: Use MonotonicClock for durations
var deadline = MonoDeadline.In(monotonicClock, TimeSpan.FromSeconds(30));
if (deadline.Expired(monotonicClock)) // Stable timing
```

## Summary

- **`TimeProvider`** = "What time is it?" (wall clock, authoritative timestamps)
- **`IMonotonicClock`** = "How much time has passed?" (stable durations, timeouts)

Choose the right tool for the job to ensure your code is both correct and testable.