# Work Queue Pattern Implementation

This document describes the integrated work queue pattern implementation that provides reliable, atomic claim-and-process semantics directly within the existing domain-specific interfaces.

## Architecture Overview

The work queue functionality has been **integrated directly into the existing domain interfaces** rather than creating separate generic queue clients. This eliminates confusion about which API to use and ensures there's only one way to process each type of work item.

### Key Design Principles

1. **Domain Integration**: Work queue methods are part of the primary domain interfaces (`IOutbox`, `ISchedulerClient`)
2. **Single Responsibility**: Each interface handles both enqueueing and processing for its specific domain
3. **No Generic Abstractions**: No separate `IWorkQueueClient<T>` or base classes to understand
4. **Clear API Surface**: One obvious way to claim, process, and acknowledge work items

## Interfaces

### IOutbox - Outbox Pattern with Work Queue

```csharp
public interface IOutbox
{
    // Traditional outbox enqueue (transactional)
    Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId = null);

    // Work queue operations (non-transactional)
    Task<IReadOnlyList<Guid>> ClaimAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken = default);
    Task AckAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AbandonAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task FailAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task ReapExpiredAsync(CancellationToken cancellationToken = default);
}
```

### ISchedulerClient - Timer and Job Processing

```csharp
public interface ISchedulerClient
{
    // Traditional scheduling operations
    Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime);
    Task<bool> CancelTimerAsync(string timerId);
    Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload = null);
    Task DeleteJobAsync(string jobName);
    Task TriggerJobAsync(string jobName);

    // Work queue operations for timers
    Task<IReadOnlyList<Guid>> ClaimTimersAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken = default);
    Task AckTimersAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AbandonTimersAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task ReapExpiredTimersAsync(CancellationToken cancellationToken = default);

    // Work queue operations for job runs
    Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken = default);
    Task AckJobRunsAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AbandonJobRunsAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken = default);
}
```

## Database Schema

The work queue pattern adds these columns to existing tables:

```sql
-- Added to infra.Outbox, infra.Timers, infra.JobRuns
Status TINYINT NOT NULL DEFAULT(0)           -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
LockedUntil DATETIME2(3) NULL                -- UTC lease expiration time  
OwnerToken UNIQUEIDENTIFIER NULL             -- Process ownership identifier
```

**Indexes for efficient work queue operations:**
```sql
CREATE INDEX IX_Outbox_WorkQueue ON infra.Outbox(Status, CreatedAt) INCLUDE(Id, OwnerToken);
CREATE INDEX IX_Timers_WorkQueue ON infra.Timers(StatusCode, DueTime) INCLUDE(Id, OwnerToken);
CREATE INDEX IX_JobRuns_WorkQueue ON infra.JobRuns(StatusCode, ScheduledTime) INCLUDE(Id, OwnerToken);
```

## Stored Procedures

**Per-table procedures are generated following this pattern:**

- `{Table}_Claim` - Atomically claims ready items with lease
- `{Table}_Ack` - Marks items as successfully completed  
- `{Table}_Abandon` - Returns items to ready state for retry
- `{Table}_Fail` - Marks items as failed
- `{Table}_ReapExpired` - Recovers expired leases

**Example for Outbox:**
```sql
infra.Outbox_Claim
infra.Outbox_Ack  
infra.Outbox_Abandon
infra.Outbox_Fail
infra.Outbox_ReapExpired
```

## Usage Examples

### Outbox Processing Worker

```csharp
public class OutboxWorker : BackgroundService
{
    private readonly IOutbox outbox;
    private readonly Guid ownerToken = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Claim work items
            var claimedIds = await outbox.ClaimAsync(ownerToken, 30, 50, stoppingToken);
            if (claimedIds.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            var succeededIds = new List<Guid>();
            var failedIds = new List<Guid>();

            // Process each item
            foreach (var id in claimedIds)
            {
                try
                {
                    await ProcessOutboxMessageAsync(id, stoppingToken);
                    succeededIds.Add(id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process outbox item {Id}", id);
                    failedIds.Add(id);
                }
            }

            // Acknowledge results
            if (succeededIds.Count > 0)
                await outbox.AckAsync(ownerToken, succeededIds, stoppingToken);

            if (failedIds.Count > 0)
                await outbox.AbandonAsync(ownerToken, failedIds, stoppingToken);
        }
    }
}
```

### Timer Processing Worker

```csharp
public class TimerWorker : BackgroundService
{
    private readonly ISchedulerClient scheduler;
    private readonly Guid ownerToken = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Claim due timers
            var claimedIds = await scheduler.ClaimTimersAsync(ownerToken, 30, 20, stoppingToken);
            if (claimedIds.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            // Process timers...
            var processedIds = await ProcessTimersAsync(claimedIds, stoppingToken);
            
            // Acknowledge completed timers
            await scheduler.AckTimersAsync(ownerToken, processedIds, stoppingToken);
        }
    }
}
```

## Key Benefits of This Approach

### 1. **Simplified API Surface**
- No confusion about which client to use
- Work queue operations are naturally part of the domain interface
- One obvious way to process each type of work item

### 2. **Domain-Specific Behavior** 
- Timers are claimed only when `DueTime <= now()`
- Job runs are claimed only when `ScheduledTime <= now()`
- Outbox items are claimed immediately when ready

### 3. **Clear Ownership Model**
- Each domain interface owns its complete lifecycle
- No leaky abstractions or generic base classes
- Easy to understand and maintain

### 4. **Production Ready**
- Atomic operations using SQL Server lock hints
- Lease-based processing with automatic recovery
- Battle-tested concurrency patterns
- Comprehensive error handling

## Migration from Previous Generic Implementation

If you were using the previous generic work queue clients:

**Before:**
```csharp
var outboxClient = new OutboxWorkQueueClient(connectionString);
var claimedIds = await outboxClient.ClaimAsync(ownerToken, 30, 10);
```

**After:**
```csharp
var outbox = serviceProvider.GetRequiredService<IOutbox>();
var claimedIds = await outbox.ClaimAsync(ownerToken, 30, 10);
```

The work queue functionality is now **integrated directly into the existing domain services** you're already using.

## Concurrency and Safety

- **Atomic Claims**: Using `READPAST, UPDLOCK, ROWLOCK` hints prevent blocking
- **Owner Token Validation**: Only the claiming process can modify items
- **Lease-Based Processing**: Automatic recovery from process failures
- **Idempotent Operations**: Safe to retry on failures
- **No Long Transactions**: Short-lived connections for scalability

This implementation provides a clean, understandable API surface while maintaining all the reliability and performance characteristics required for production workloads.
