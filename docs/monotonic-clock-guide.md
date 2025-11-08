# Monotonic Clock Usage Guide

The `IMonotonicClock` provides stable, monotonic time measurements that are immune to system clock changes. This is essential for accurate timeouts, performance measurements, and lease management.

## Why Use Monotonic Time?

### The Problem with Wall Clock Time

Using `DateTime.UtcNow` or `DateTimeOffset.UtcNow` for timeouts and durations can cause issues:

```csharp
// ❌ PROBLEMATIC: Wall clock can jump backwards or forwards
var deadline = DateTime.UtcNow.AddSeconds(30);

while (DateTime.UtcNow < deadline)
{
    // This loop might exit early if clock jumps forward (NTP adjustment)
    // Or run forever if clock jumps backward
    await TryOperationAsync();
}
```

**Problems:**
- **NTP adjustments** - System clock syncs can jump time forward or backward
- **Daylight saving** - Clock changes during transition periods
- **Manual changes** - User or admin adjusts system time
- **GC pauses** - Long garbage collections aren't reflected in wall time
- **Virtualization** - VM time can drift or jump when suspended

### The Solution: Monotonic Time

Monotonic clocks measure elapsed time using a stable, ever-increasing counter:

```csharp
// ✅ CORRECT: Monotonic time is stable and reliable
var deadline = MonoDeadline.In(_clock, TimeSpan.FromSeconds(30));

while (!deadline.Expired(_clock))
{
    // This loop will reliably exit after 30 seconds of elapsed time
    // regardless of system clock changes
    await TryOperationAsync();
}
```

## Installation and Setup

The `IMonotonicClock` is automatically registered when you add platform services:

```csharp
using Bravellian.Platform;

var builder = WebApplication.CreateBuilder(args);

// Any of these will register IMonotonicClock
builder.Services.AddSqlOutbox(options);
builder.Services.AddSqlInbox(options);
builder.Services.AddSqlScheduler(options);

// Or register it explicitly
builder.Services.AddSingleton<IMonotonicClock, MonotonicClock>();
```

## Basic Usage

### Creating Timeouts

Use `MonoDeadline` to create timeout points:

```csharp
public class ApiClient
{
    private readonly IMonotonicClock _clock;
    private readonly HttpClient _httpClient;

    public ApiClient(IMonotonicClock clock, HttpClient httpClient)
    {
        _clock = clock;
        _httpClient = httpClient;
    }

    public async Task<ApiResponse> CallWithTimeoutAsync(
        string url, 
        TimeSpan timeout)
    {
        // Create a deadline 30 seconds from now
        var deadline = MonoDeadline.In(_clock, timeout);

        while (!deadline.Expired(_clock))
        {
            try
            {
                // Try the API call
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                return await response.Content.ReadFromJsonAsync<ApiResponse>();
            }
            catch (HttpRequestException ex) when (!deadline.Expired(_clock))
            {
                // Transient error - retry if we have time
                _logger.LogWarning(ex, "API call failed, retrying...");
                
                // Brief delay before retry
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException($"API call to {url} timed out after {timeout}");
    }
}
```

### Measuring Elapsed Time

Measure how long operations take:

```csharp
public class PerformanceMonitor
{
    private readonly IMonotonicClock _clock;

    public async Task<OperationMetrics> MeasureOperationAsync(
        Func<Task> operation)
    {
        // Record start time
        var startTime = _clock.Seconds;

        try
        {
            await operation();
            
            // Calculate elapsed time
            var elapsedSeconds = _clock.Seconds - startTime;
            
            return new OperationMetrics
            {
                Success = true,
                Duration = TimeSpan.FromSeconds(elapsedSeconds)
            };
        }
        catch (Exception ex)
        {
            var elapsedSeconds = _clock.Seconds - startTime;
            
            return new OperationMetrics
            {
                Success = false,
                Duration = TimeSpan.FromSeconds(elapsedSeconds),
                Error = ex.Message
            };
        }
    }
}
```

### Rate Limiting

Implement rate limiting with monotonic time:

```csharp
public class RateLimiter
{
    private readonly IMonotonicClock _clock;
    private readonly int _maxRequestsPerSecond;
    private readonly Queue<double> _requestTimes = new();

    public RateLimiter(IMonotonicClock clock, int maxRequestsPerSecond)
    {
        _clock = clock;
        _maxRequestsPerSecond = maxRequestsPerSecond;
    }

    public async Task<bool> TryAcquireAsync()
    {
        var now = _clock.Seconds;
        var windowStart = now - 1.0; // 1 second window

        // Remove requests outside the window
        while (_requestTimes.Count > 0 && _requestTimes.Peek() < windowStart)
        {
            _requestTimes.Dequeue();
        }

        // Check if we're at the limit
        if (_requestTimes.Count >= _maxRequestsPerSecond)
        {
            // Calculate how long to wait
            var oldestRequest = _requestTimes.Peek();
            var waitSeconds = oldestRequest + 1.0 - now;
            
            if (waitSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            }
            
            return false; // Caller should retry
        }

        // Record this request
        _requestTimes.Enqueue(now);
        return true;
    }
}
```

## Advanced Patterns

### Lease Renewal with Monotonic Scheduling

This is the pattern used internally by `LeaseRunner`:

```csharp
public class AutoRenewingLease : IAsyncDisposable
{
    private readonly IMonotonicClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ILeaseApi _leaseApi;
    private readonly CancellationTokenSource _cts = new();
    private Task? _renewalTask;

    public async Task<AutoRenewingLease?> AcquireAsync(
        string leaseName,
        TimeSpan leaseDuration,
        double renewPercent = 0.6)
    {
        // Try to acquire lease using wall clock for database
        var result = await _leaseApi.AcquireAsync(
            leaseName,
            _timeProvider.GetUtcNow(),
            leaseDuration);

        if (!result.Acquired)
        {
            return null;
        }

        // Schedule renewal using monotonic clock for stability
        var renewalInterval = leaseDuration * renewPercent;
        var nextRenewal = MonoDeadline.In(_clock, renewalInterval);

        // Start background renewal
        _renewalTask = RenewalLoopAsync(
            leaseName, 
            leaseDuration, 
            renewalInterval, 
            nextRenewal);

        return this;
    }

    private async Task RenewalLoopAsync(
        string leaseName,
        TimeSpan leaseDuration,
        TimeSpan renewalInterval,
        MonoDeadline nextRenewal)
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            // Check if it's time to renew
            if (nextRenewal.Expired(_clock))
            {
                // Add small random jitter to prevent thundering herd
                var jitterMs = Random.Shared.Next(0, 1000);
                await Task.Delay(jitterMs, _cts.Token);

                // Attempt renewal
                var renewed = await _leaseApi.RenewAsync(
                    leaseName,
                    _timeProvider.GetUtcNow(),
                    leaseDuration);

                if (!renewed)
                {
                    // Lost the lease
                    _logger.LogWarning("Failed to renew lease {LeaseName}", leaseName);
                    _cts.Cancel();
                    break;
                }

                // Schedule next renewal
                nextRenewal = MonoDeadline.In(_clock, renewalInterval);
            }

            // Check frequently for cancellation
            await Task.Delay(100, _cts.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        
        if (_renewalTask != null)
        {
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts.Dispose();
    }
}
```

### Exponential Backoff with Jitter

Implement retry with exponential backoff:

```csharp
public class ResilientOperation
{
    private readonly IMonotonicClock _clock;

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 5)
    {
        var attempt = 0;
        var baseDelay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (TransientException ex) when (attempt < maxAttempts - 1)
            {
                attempt++;
                
                // Calculate exponential backoff with jitter
                var exponentialDelay = TimeSpan.FromSeconds(
                    Math.Pow(2, attempt) * baseDelay.TotalSeconds);
                
                var jitter = TimeSpan.FromMilliseconds(
                    Random.Shared.Next(0, 1000));
                
                var totalDelay = exponentialDelay + jitter;

                _logger.LogWarning(ex,
                    "Attempt {Attempt} failed, retrying after {Delay}",
                    attempt, totalDelay);

                // Use monotonic time for delay measurement
                var deadline = MonoDeadline.In(_clock, totalDelay);
                
                while (!deadline.Expired(_clock))
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
```

### Circuit Breaker Pattern

Implement a circuit breaker with monotonic time:

```csharp
public class CircuitBreaker
{
    private readonly IMonotonicClock _clock;
    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    
    private int _consecutiveFailures;
    private double _openedAt;
    private CircuitState _state = CircuitState.Closed;

    public CircuitBreaker(
        IMonotonicClock clock,
        int failureThreshold = 5,
        TimeSpan? resetTimeout = null)
    {
        _clock = clock;
        _failureThreshold = failureThreshold;
        _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(60);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        // Check if circuit should transition from open to half-open
        if (_state == CircuitState.Open)
        {
            var elapsed = _clock.Seconds - _openedAt;
            if (elapsed >= _resetTimeout.TotalSeconds)
            {
                _state = CircuitState.HalfOpen;
                _logger.LogInformation("Circuit breaker entering half-open state");
            }
            else
            {
                throw new CircuitBreakerOpenException(
                    $"Circuit is open. Retry after {_resetTimeout.TotalSeconds - elapsed:F1}s");
            }
        }

        try
        {
            var result = await operation();
            
            // Success - close circuit if it was half-open
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _consecutiveFailures = 0;
                _logger.LogInformation("Circuit breaker closed after successful operation");
            }
            
            return result;
        }
        catch (Exception)
        {
            _consecutiveFailures++;
            
            if (_consecutiveFailures >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _openedAt = _clock.Seconds;
                _logger.LogWarning(
                    "Circuit breaker opened after {Failures} consecutive failures",
                    _consecutiveFailures);
            }
            
            throw;
        }
    }

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}
```

### Background Service with Monotonic Timing

Use monotonic time in background services:

```csharp
public class HealthCheckService : BackgroundService
{
    private readonly IMonotonicClock _clock;
    private readonly IHealthChecker _healthChecker;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCheck = MonoDeadline.In(_clock, _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (nextCheck.Expired(_clock))
            {
                // Perform health check
                var startTime = _clock.Seconds;
                var isHealthy = await _healthChecker.CheckAsync(stoppingToken);
                var duration = _clock.Seconds - startTime;

                _logger.LogInformation(
                    "Health check completed in {Duration:F2}s: {Status}",
                    duration,
                    isHealthy ? "Healthy" : "Unhealthy");

                // Schedule next check
                nextCheck = MonoDeadline.In(_clock, _checkInterval);
            }

            // Sleep briefly before checking again
            await Task.Delay(100, stoppingToken);
        }
    }
}
```

## API Reference

### IMonotonicClock Interface

```csharp
public interface IMonotonicClock
{
    /// <summary>
    /// Gets the current monotonic time in high-resolution ticks.
    /// </summary>
    long Ticks { get; }

    /// <summary>
    /// Gets the current monotonic time in seconds.
    /// </summary>
    double Seconds { get; }
}
```

### MonoDeadline Struct

```csharp
public readonly struct MonoDeadline
{
    public double AtSeconds { get; }

    /// <summary>
    /// Creates a deadline at a specific time.
    /// </summary>
    public MonoDeadline(double atSeconds);

    /// <summary>
    /// Creates a deadline after a duration from now.
    /// </summary>
    public static MonoDeadline In(IMonotonicClock clock, TimeSpan duration);

    /// <summary>
    /// Creates a deadline at a specific absolute time.
    /// </summary>
    public static MonoDeadline At(double atSeconds);

    /// <summary>
    /// Checks if this deadline has expired.
    /// </summary>
    public bool Expired(IMonotonicClock clock);

    /// <summary>
    /// Gets remaining time until deadline.
    /// </summary>
    public TimeSpan TimeRemaining(IMonotonicClock clock);
}
```

## When NOT to Use Monotonic Time

Use `TimeProvider` (wall clock) instead of `IMonotonicClock` for:

- **Database timestamps** - CreatedAt, UpdatedAt, ScheduledAt fields
- **Business logic dates** - Order dates, invoice dates, appointment times
- **API responses** - Timestamps returned to clients
- **Audit logs** - When did an event actually occur
- **Scheduling** - "Run this job at 3 PM tomorrow"

**Example:**
```csharp
// ❌ WRONG: Don't use monotonic time for database timestamps
var order = new Order
{
    CreatedAt = _clock.Seconds // This is meaningless!
};

// ✅ CORRECT: Use TimeProvider for timestamps
var order = new Order
{
    CreatedAt = _timeProvider.GetUtcNow()
};
```

## Testing with Fake Clocks

### Production Code

```csharp
public class CacheEntry
{
    private readonly IMonotonicClock _clock;
    private readonly double _expiresAt;

    public CacheEntry(IMonotonicClock clock, TimeSpan ttl)
    {
        _clock = clock;
        _expiresAt = clock.Seconds + ttl.TotalSeconds;
    }

    public bool IsExpired => _clock.Seconds >= _expiresAt;
}
```

### Test Code

```csharp
public class FakeMonotonicClock : IMonotonicClock
{
    private double _currentSeconds;

    public long Ticks => (long)(_currentSeconds * Stopwatch.Frequency);
    public double Seconds => _currentSeconds;

    public void Advance(TimeSpan duration)
    {
        _currentSeconds += duration.TotalSeconds;
    }
}

[Fact]
public void CacheEntry_Expires_AfterTTL()
{
    // Arrange
    var fakeClock = new FakeMonotonicClock();
    var entry = new CacheEntry(fakeClock, TimeSpan.FromSeconds(30));

    // Assert - not expired initially
    Assert.False(entry.IsExpired);

    // Act - advance time
    fakeClock.Advance(TimeSpan.FromSeconds(31));

    // Assert - now expired
    Assert.True(entry.IsExpired);
}
```

## Implementation Details

The `MonotonicClock` class uses `Stopwatch.GetTimestamp()` internally:

```csharp
internal sealed class MonotonicClock : IMonotonicClock
{
    private static readonly double TicksToSeconds = 1.0 / Stopwatch.Frequency;

    public long Ticks => Stopwatch.GetTimestamp();
    
    public double Seconds => Ticks * TicksToSeconds;
}
```

**Why `Stopwatch`?**
- Available on all .NET platforms
- High resolution (typically microseconds or better)
- Monotonic by design (never goes backward)
- Efficient (direct CPU counter access)

## Performance Considerations

### Overhead

Monotonic clock access is extremely fast:
- **Cost**: ~10-20 nanoseconds per call
- **Implementation**: Direct CPU instruction (RDTSC or equivalent)
- **Comparison**: ~1000x faster than `DateTime.UtcNow`

### Best Practices

```csharp
// ✅ GOOD: Store start time, calculate duration later
var startTime = _clock.Seconds;
await DoWorkAsync();
var duration = _clock.Seconds - startTime;

// ❌ UNNECESSARY: Don't capture clock too frequently
for (int i = 0; i < 1000000; i++)
{
    var now = _clock.Seconds; // Wasteful in tight loop
    ProcessItem(i);
}

// ✅ BETTER: Check periodically
var checkInterval = MonoDeadline.In(_clock, TimeSpan.FromSeconds(1));
for (int i = 0; i < 1000000; i++)
{
    ProcessItem(i);
    
    if (checkInterval.Expired(_clock))
    {
        // Check for cancellation, log progress, etc.
        checkInterval = MonoDeadline.In(_clock, TimeSpan.FromSeconds(1));
    }
}
```

## Summary

Use `IMonotonicClock` for:

✅ **Timeouts and deadlines** - Reliable timeout checking  
✅ **Performance measurement** - Accurate duration tracking  
✅ **Rate limiting** - Stable time windows  
✅ **Lease renewals** - Periodic background tasks  
✅ **Retry delays** - Exponential backoff timing  

Avoid it for:

❌ **Timestamps** - Use `TimeProvider.GetUtcNow()`  
❌ **Scheduling** - Use `TimeProvider` for wall clock times  
❌ **Business logic** - Use `TimeProvider` for actual dates  

The combination of `TimeProvider` (for what time it is) and `IMonotonicClock` (for how much time has passed) provides a complete, testable time abstraction for building reliable distributed systems.

## Related Documentation

- [Time Abstractions Overview](time-abstractions.md) - When to use which abstraction
- [Lease System v2](lease-v2-usage.md) - Real-world usage in distributed locking
- [Work Queue Pattern](work-queue-pattern.md) - Lease-based processing with timeouts
