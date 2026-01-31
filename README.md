# Bravellian Platform

A robust .NET platform providing SQL-based distributed locking, outbox pattern implementation, and scheduler services for building resilient, scalable applications. The platform is designed around **work queue patterns** with **claim-ack-abandon semantics** and **database-authoritative timing** for maximum reliability in distributed systems.

## Overview

This platform provides five core components that work together seamlessly:

1. **SQL Distributed Lock** - Database-backed distributed locking with lease renewal
2. **Outbox Service** - Transactional outbox pattern with work queue processing
3. **Inbox Service** - At-most-once processing guard for inbound messages  
4. **Timer Scheduler** - One-time scheduled tasks with precise timing
5. **Job Scheduler** - Recurring jobs with cron-based scheduling

**Key Design Principles:**
- **Database-Authoritative Timing**: SQL Server's `SYSUTCDATETIME()` is the source of truth
- **Monotonic Clock Scheduling**: Resilient to system clock changes and GC pauses
- **Work Queue Pattern**: Atomic claim-process-acknowledge lifecycle for reliability
- **Integrated API Design**: Work queue operations are part of domain interfaces
- **Strong Consistency**: All operations leverage SQL Server's ACID properties

## Quick Start

### Installation

Add the package to your project:

```bash
dotnet add package Bravellian.Platform
```

### Basic Setup

```csharp
using Bravellian.Platform;

// In your Program.cs or Startup.cs
var builder = WebApplication.CreateBuilder(args);

// Option 1: Add complete scheduler platform (recommended)
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true, // Automatically creates tables
    MaxPollingInterval = TimeSpan.FromSeconds(5)
});

// Option 2: Add components individually with more control
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true
});

builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck();

var app = builder.Build();

// Configure health check endpoint
app.MapHealthChecks("/health");
```

### Discovery-first feature registration

When you are already using `IPlatformDatabaseDiscovery` to enumerate tenant databases, you can opt into unified, discovery-based
feature registration without bringing in the full platform bootstrapper. The following helpers reuse the same discovery pipeline
for Outbox, Inbox, Scheduler, Fanout, and Leases:

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services
    .AddPlatformOutbox(enableSchemaDeployment: true)
    .AddPlatformInbox(enableSchemaDeployment: true)
    .AddPlatformScheduler()
    .AddPlatformFanout()
    .AddPlatformLeases();
```

### Database Schema

The platform automatically creates the required database schema when `EnableSchemaDeployment = true`. Alternatively, you can run the SQL scripts manually:

```bash
# Core tables and stored procedures
src/Bravellian.Platform.SqlServer/Database/
├── Outbox.sql                    # Message outbox table
├── OutboxWorkQueueProcs.sql      # Outbox work queue procedures
├── Timers.sql                    # One-time scheduled tasks
├── TimersWorkQueueProcs.sql      # Timer work queue procedures  
├── Jobs.sql                      # Recurring job definitions
├── JobRuns.sql                   # Job execution instances
├── JobRunsWorkQueueProcs.sql     # Job run work queue procedures
└── Inbox.sql                     # Inbound message deduplication
```

**Work Queue Enhancement**: All tables include work queue columns (`Status`, `LockedUntil`, `OwnerToken`) for atomic claim-and-process semantics.

## 📚 Documentation

**New to the platform?** Start with our [Getting Started Guide](docs/GETTING_STARTED.md)!

**Quick Start Guides:**
- [Outbox Pattern Quick Start](docs/outbox-quickstart.md) - Reliable message publishing
- [Inbox Pattern Quick Start](docs/inbox-quickstart.md) - Idempotent message processing
- [Monotonic Clock Guide](docs/monotonic-clock-guide.md) - Stable timing for timeouts and measurements
- [Health Probe CLI](src/Bravellian.Platform.HealthProbe/README.md) - Deploy-time healthcheck CLI setup and usage

**API References:**
- [Outbox API Reference](docs/outbox-api-reference.md) - Complete IOutbox documentation
- [Inbox API Reference](docs/inbox-api-reference.md) - Complete IInbox documentation

**Core Concepts:**
- [Platform Primitives Overview](docs/platform-primitives-overview.md) - Inbox, outbox, fanout, and fan-in lifecycle
- [Time Abstractions](docs/time-abstractions.md) - TimeProvider vs IMonotonicClock
- [Work Queue Pattern](docs/work-queue-pattern.md) - Claim-ack-abandon semantics
- [Observability Guide](docs/observability/README.md) - Audit, operations, and observability conventions

**Multi-Tenant Scenarios:**
- [Outbox Router](docs/OutboxRouter.md) - Multi-database outbox routing
- [Inbox Router](docs/InboxRouter.md) - Multi-database inbox routing
- [Multi-Database Pattern](docs/multi-database-pattern.md) - Comprehensive guide

**Additional Resources:**
- [Documentation Index](docs/INDEX.md) - Complete documentation catalog
- [Lease System v2](docs/lease-v2-usage.md) - Distributed locking
- [Schema Configuration](docs/schema-configuration.md) - Database setup

## Architecture Deep Dive

### Work Queue Pattern Implementation

The platform's reliability comes from its **integrated work queue pattern** that provides atomic claim-and-process semantics for all background operations.

#### Core Concepts

**1. Atomic Claims**: Using SQL Server's `READPAST, UPDLOCK, ROWLOCK` hints, workers atomically claim items without blocking other processes.

**2. Lease-Based Processing**: Claimed items have a lease timeout (`LockedUntil`). If a worker crashes, items automatically become available for retry when the lease expires.

**3. Owner Token Validation**: Each worker process uses a unique `OwnerToken` (GUID) to ensure only the claiming process can modify its items.

#### Work Item Lifecycle

```
Ready (Status=0) → Claim → InProgress (Status=1) → Process → Done (Status=2)
                             ↓                       ↑
                         Abandon/Fail         Automatic Reap
                             ↓                       ↑
                      Ready (Status=0) ←──────────────┘
```

**State Transitions:**
- **Claim**: `Ready → InProgress` (atomic, with lease)
- **Acknowledge**: `InProgress → Done` (successful completion)
- **Abandon**: `InProgress → Ready` (temporary failure, retry later)
- **Fail**: `InProgress → Failed` (permanent failure)
- **Reap**: `InProgress → Ready` (lease expired, automatic recovery)

#### Database Schema Design

All work queue tables share these common columns:

```sql
-- Work queue state management
Status TINYINT NOT NULL DEFAULT(0),           -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
LockedUntil DATETIME2(3) NULL,                -- UTC lease expiration time  
OwnerToken UNIQUEIDENTIFIER NULL              -- Process ownership identifier

-- Optimized indexes for work queue operations
CREATE INDEX IX_{Table}_WorkQueue ON infra.{Table}(Status, {TimeColumn}) 
    INCLUDE(Id, OwnerToken);
```

#### Stored Procedures Pattern

Each table has five generated stored procedures following this pattern:

```sql
-- Example for Outbox table
infra.Outbox_Claim        -- Atomically claim ready items
infra.Outbox_Ack          -- Mark items as successfully completed
infra.Outbox_Abandon      -- Return items to ready state for retry
infra.Outbox_Fail         -- Mark items as failed
infra.Outbox_ReapExpired  -- Recover expired leases
```

**Claim Procedure Logic** (simplified):
```sql
CREATE OR ALTER PROCEDURE infra.Outbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM infra.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0 /* Ready */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET 
        Status = 1 /* InProgress */, 
        OwnerToken = @OwnerToken, 
        LockedUntil = @until
    OUTPUT inserted.Id
    FROM infra.Outbox o
    JOIN cte ON cte.Id = o.Id;
END
```

### Time Abstractions System

The platform uses two complementary time abstractions for reliability:

#### TimeProvider - Wall Clock Authority
- **Purpose**: Authoritative timestamps and business logic timing
- **Usage**: Database records, audit trails, scheduling due times
- **Implementation**: Uses `TimeProvider.System` in production
- **Database Authority**: `SYSUTCDATETIME()` is the source of truth

#### IMonotonicClock - Stable Duration Measurement  
- **Purpose**: Timeout handling, lease renewals, performance measurement
- **Usage**: Background service timing, lease management, retry delays
- **Implementation**: Platform-specific monotonic time source
- **Resilience**: Immune to system clock changes, NTP adjustments, GC pauses

#### Example: Lease Renewal with Monotonic Timing

```csharp
public class LeaseRunner
{
    private readonly IMonotonicClock _clock;
    private readonly TimeProvider _timeProvider;

    public async Task<LeaseRunner> AcquireAsync(
        string leaseName, 
        TimeSpan leaseDuration, 
        double renewPercent = 0.6)
    {
        // Use wall clock for database operations
        var result = await _leaseApi.AcquireAsync(leaseName, 
            _timeProvider.GetUtcNow(), leaseDuration);
        
        if (!result.Acquired) return null;

        // Use monotonic clock for renewal scheduling
        var renewalInterval = leaseDuration * renewPercent;
        var nextRenewal = MonoDeadline.In(_clock, renewalInterval);
        
        StartRenewalLoop(nextRenewal);
        return this;
    }
}
```

### Background Service Architecture

#### Polling Services with Exponential Backoff

Background services use intelligent polling with automatic backoff:

```csharp
public class OutboxPollingService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var emptyCount = 0;
        const double baseInterval = 0.25; // 250ms base
        const double maxInterval = 30.0;  // 30s max

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await _dispatcher.RunOnceAsync(batchSize: 50, stoppingToken);
            
            if (processed > 0)
            {
                emptyCount = 0; // Reset backoff on successful processing
                continue;
            }
            
            // Exponential backoff when no work available
            emptyCount++;
            var delay = Math.Min(baseInterval * Math.Pow(2, emptyCount), maxInterval);
            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
```

#### Schema Deployment Service

```csharp
public class DatabaseSchemaBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Deploy schemas for all enabled components
        var deploymentTasks = new List<Task>();
        
        if (_outboxOptions.CurrentValue.EnableSchemaDeployment)
            deploymentTasks.Add(DeployOutboxSchemaAsync());
            
        if (_schedulerOptions.CurrentValue.EnableSchemaDeployment)
            deploymentTasks.Add(DeploySchedulerSchemaAsync());
            
        await Task.WhenAll(deploymentTasks);
        
        // Signal completion to dependent services
        _schemaCompletion.SetCompleted();
    }
}
```

## Components

## 1. SQL Distributed Lock (Lease System v2)

The distributed lock system provides database-authoritative lease management with monotonic clock scheduling and automatic renewal.

### Architecture

**Core Components:**
- **`infra.Lease` table**: Stores lease information with DB-authoritative timestamps
- **`LeaseApi`**: Low-level data access operations  
- **`LeaseRunner`**: High-level lease manager with automatic renewal

### Key Features

1. **DB-Authoritative Timestamps**: Database decides lease validity using `SYSUTCDATETIME()`
2. **Monotonic Scheduling**: Uses `IMonotonicClock` for renewal timing
3. **Configurable Renewal**: Renews at configurable percentage of lease duration (default 60%)
4. **Jitter Prevention**: Adds random jitter to prevent thundering herd
5. **Server Time Synchronization**: Returns server time to reduce round-trips

### Basic Usage

```csharp
public class CriticalProcessService
{
    private readonly LeaseApi _leaseApi;
    private readonly IMonotonicClock _clock;
    private readonly TimeProvider _timeProvider;

    public async Task RunExclusiveProcessAsync()
    {
        // Acquire a 5-minute lease with 60% renewal threshold
        var runner = await LeaseRunner.AcquireAsync(
            _leaseApi, _clock, _timeProvider,
            leaseName: "critical-process",
            owner: Environment.MachineName,
            leaseDuration: TimeSpan.FromMinutes(5),
            renewPercent: 0.6);

        if (runner == null)
        {
            // Another instance is already running
            _logger.LogInformation("Critical process already running on another instance");
            return;
        }

        try
        {
            // Do exclusive work - use runner.CancellationToken for cooperative cancellation
            await ProcessDataAsync(runner.CancellationToken);
        }
        catch (LostLeaseException ex)
        {
            _logger.LogWarning("Lease lost during processing: {Message}", ex.Message);
        }
        finally
        {
            await runner.DisposeAsync(); // Releases lease
        }
    }
}
```

### Advanced Lease Management

```csharp
public class LongRunningJobService
{
    public async Task ProcessLargeDatasetAsync()
    {
        var runner = await LeaseRunner.AcquireAsync(/* ... */);
        if (runner == null) return;

        try
        {
            var items = await GetItemsToProcessAsync();
            
            foreach (var item in items)
            {
                // Check lease before each item
                runner.ThrowIfLost();
                
                await ProcessItemAsync(item);
                
                // Manually renew if needed (optional - automatic renewal happens in background)
                if (ShouldRenewNow())
                {
                    var renewed = await runner.TryRenewNowAsync();
                    if (!renewed)
                    {
                        _logger.LogWarning("Failed to renew lease, stopping processing");
                        break;
                    }
                }
            }
        }
        finally
        {
            await runner.DisposeAsync();
        }
    }
}
```

### Lease Renewal Mechanics

The `LeaseRunner` automatically handles lease renewal:

```csharp
// Internal renewal logic (simplified)
private async Task RenewalLoopAsync()
{
    while (!_cancellationTokenSource.Token.IsCancellationRequested)
    {
        // Calculate next renewal time using monotonic clock
        var renewAt = _nextRenewalDeadline;
        var now = _monotonicClock.Seconds;
        
        if (now >= renewAt.AtSeconds)
        {
            // Attempt renewal with jitter to prevent herd behavior
            var jitter = Random.Shared.NextDouble(); // 0-1 second
            await Task.Delay(TimeSpan.FromSeconds(jitter));
            
            var result = await _leaseApi.RenewAsync(_leaseName, _owner, _leaseDurationSeconds);
            
            if (result.Renewed)
            {
                // Schedule next renewal at 60% of lease duration
                var renewalInterval = _leaseDurationSeconds * _renewPercent;
                _nextRenewalDeadline = MonoDeadline.In(_monotonicClock, 
                    TimeSpan.FromSeconds(renewalInterval));
            }
            else
            {
                // Lease lost - cancel all operations
                _cancellationTokenSource.Cancel();
                break;
            }
        }
        
        await Task.Delay(TimeSpan.FromSeconds(0.1)); // Check every 100ms
    }
}
```

## 2. Outbox Service

Implements the transactional outbox pattern with integrated work queue processing for reliable message publishing alongside database transactions.

### Architecture

The outbox service combines traditional enqueuing with work queue processing:

**Enqueue Phase**: Messages are stored in the outbox table within business transactions
**Process Phase**: Background workers claim and process messages using work queue pattern

### Database Schema

```sql
CREATE TABLE infra.Outbox (
    -- Core message fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Work queue state management
    Status TINYINT NOT NULL DEFAULT(0),           -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,                -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,             -- Process ownership identifier
    
    -- Processing metadata
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL,
    
    -- Error handling and retry
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Message tracking
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CorrelationId NVARCHAR(255) NULL
);

-- Optimized work queue index
CREATE INDEX IX_Outbox_WorkQueue ON infra.Outbox(Status, CreatedAt) 
    INCLUDE(Id, OwnerToken);
```

### Basic Usage

```csharp
public class OrderService
{
    private readonly ISqlServerContext _dbContext;
    private readonly IOutbox _outbox;

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        try
        {
            // 1. Perform business operations
            var order = new Order 
            { 
                CustomerId = request.CustomerId,
                Total = request.Total,
                CreatedAt = DateTime.UtcNow
            };
            
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            // 2. Enqueue outbox message in same transaction
            await _outbox.EnqueueAsync(
                topic: "OrderCreated",
                payload: JsonSerializer.Serialize(new OrderCreatedEvent 
                { 
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Total = order.Total,
                    CreatedAt = order.CreatedAt
                }),
                transaction: transaction.GetDbTransaction(),
                correlationId: request.CorrelationId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

### Standalone Usage (Self-Managed Transaction)

```csharp
public class NotificationService
{
    private readonly IOutbox _outbox;

    public async Task SendWelcomeEmailAsync(string userId, string email)
    {
        // This creates its own connection and transaction
        await _outbox.EnqueueAsync(
            topic: "WelcomeEmail",
            payload: JsonSerializer.Serialize(new WelcomeEmailEvent 
            { 
                UserId = userId,
                Email = email,
                RequestedAt = DateTime.UtcNow
            }),
            correlationId: $"welcome-{userId}");
    }
}
```

### Work Queue Processing

The outbox service provides work queue methods for building custom processors:

```csharp
public class OutboxProcessorService : BackgroundService
{
    private readonly IOutbox _outbox;
    private readonly IMessageBroker _messageBroker;
    private readonly Guid _ownerToken = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Claim batch of ready messages (30-second lease)
            var claimedIds = await _outbox.ClaimAsync(_ownerToken, 30, 50, stoppingToken);
            
            if (claimedIds.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            var succeededIds = new List<Guid>();
            var failedIds = new List<Guid>();

            // Process each claimed message
            foreach (var id in claimedIds)
            {
                try
                {
                    var message = await GetOutboxMessageAsync(id);
                    await _messageBroker.PublishAsync(message.Topic, message.Payload);
                    succeededIds.Add(id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process outbox message {Id}", id);
                    failedIds.Add(id);
                }
            }

            // Acknowledge results
            if (succeededIds.Count > 0)
                await _outbox.AckAsync(_ownerToken, succeededIds, stoppingToken);

            if (failedIds.Count > 0)
                await _outbox.AbandonAsync(_ownerToken, failedIds, stoppingToken);
        }
    }
}
```

### Built-in Background Processing

The platform includes an automatic outbox processor:

```csharp
public class OutboxPollingService : BackgroundService
{
    private readonly OutboxDispatcher _dispatcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _dispatcher.RunOnceAsync(batchSize: 50, stoppingToken);
                
                if (processed == 0)
                {
                    // No work available - exponential backoff
                    await Task.Delay(CalculateBackoffDelay(), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in outbox polling service");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
```

### Error Handling and Retry Logic

The outbox includes built-in retry with exponential backoff:

```csharp
public class OutboxDispatcher
{
    public static TimeSpan DefaultBackoff(int attempt)
    {
        // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 60s max
        var seconds = Math.Min(Math.Pow(2, attempt), 60);
        return TimeSpan.FromSeconds(seconds);
    }

    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var messages = await _store.GetNextBatchAsync(batchSize, cancellationToken);
        
        foreach (var message in messages)
        {
            try
            {
                var handler = _resolver.GetHandler(message.Topic);
                await handler.HandleAsync(message, cancellationToken);
                
                await _store.MarkProcessedAsync(message.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                var nextAttempt = _timeProvider.GetUtcNow() + _backoffPolicy(message.RetryCount);
                await _store.MarkFailedAsync(message.Id, ex.Message, nextAttempt, cancellationToken);
            }
        }
        
        return messages.Count;
    }
}
```

## 3. Inbox Service

Implements the Inbox pattern for at-most-once processing of inbound messages with atomic deduplication using SQL MERGE semantics.

### Architecture

The inbox service provides idempotent message processing by tracking message IDs and content hashes:

**First Check**: `AlreadyProcessedAsync` atomically checks if message was processed
**Safe Processing**: If new, marks as "Processing" and handles the message  
**Completion**: Marks as "Done" when successfully processed
**Poison Detection**: Tracks attempts and supports marking messages as "Dead"

### Database Schema

```sql
CREATE TABLE infra.Inbox (
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,                    -- Optional content verification
    FirstSeenUtc DATETIME2(3) NOT NULL,
    LastSeenUtc DATETIME2(3) NOT NULL,
    ProcessedUtc DATETIME2(3) NULL,
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'  -- Seen, Processing, Done, Dead
);

CREATE INDEX IX_Inbox_Processing ON infra.Inbox(Status, LastSeenUtc)
    WHERE Status IN ('Seen', 'Processing');
```

### Basic Usage

```csharp
public class OrderEventHandler
{
    private readonly IInbox _inbox;
    private readonly IOrderService _orderService;

    public async Task HandleOrderEventAsync(OrderEvent orderEvent)
    {
        // Atomic check-and-mark-as-seen operation
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId: orderEvent.MessageId,
            source: "OrderService",
            hash: ComputeHash(orderEvent)); // Optional content verification

        if (alreadyProcessed)
        {
            // Safe to ignore - already processed this exact message
            _logger.LogDebug("Message {MessageId} already processed", orderEvent.MessageId);
            return;
        }

        try
        {
            // Mark as being processed (for poison detection)
            await _inbox.MarkProcessingAsync(orderEvent.MessageId);

            // Process the message (your business logic)
            await _orderService.ProcessOrderAsync(orderEvent);

            // Mark as successfully processed
            await _inbox.MarkProcessedAsync(orderEvent.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId}", orderEvent.MessageId);
            
            // Could mark as dead after repeated failures
            await _inbox.MarkDeadAsync(orderEvent.MessageId);
            throw;
        }
    }

    private byte[] ComputeHash(OrderEvent orderEvent)
    {
        var json = JsonSerializer.Serialize(orderEvent);
        return SHA256.HashData(Encoding.UTF8.GetBytes(json));
    }
}
```

### Advanced Usage with Retry Logic

```csharp
public class ResilientMessageHandler
{
    private readonly IInbox _inbox;
    private readonly int _maxRetries = 3;

    public async Task HandleWithRetriesAsync(InboundMessage message)
    {
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            message.Id, message.Source, ComputeHash(message.Content));
            
        if (alreadyProcessed) return;

        var attempts = 0;
        Exception lastException = null;

        while (attempts < _maxRetries)
        {
            try
            {
                await _inbox.MarkProcessingAsync(message.Id);
                
                // Your processing logic here
                await ProcessMessageAsync(message);
                
                await _inbox.MarkProcessedAsync(message.Id);
                return; // Success!
            }
            catch (TransientException ex)
            {
                lastException = ex;
                attempts++;
                
                if (attempts < _maxRetries)
                {
                    // Brief delay before retry
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempts)));
                }
            }
            catch (Exception ex)
            {
                // Non-transient error - don't retry
                await _inbox.MarkDeadAsync(message.Id);
                throw;
            }
        }
        
        // Max retries exceeded
        await _inbox.MarkDeadAsync(message.Id);
        throw new MaxRetriesExceededException($"Failed after {_maxRetries} attempts", lastException);
    }
}
```

### MERGE/Upsert Semantics

The `AlreadyProcessedAsync` method uses SQL MERGE for atomic deduplication:

```sql
-- Simplified version of the actual implementation
MERGE infra.Inbox AS target
USING (VALUES (@MessageId, @Source, @Hash, @Now)) AS source(MessageId, Source, Hash, LastSeenUtc)
ON target.MessageId = source.MessageId

WHEN MATCHED THEN
    UPDATE SET 
        LastSeenUtc = source.LastSeenUtc,
        Attempts = Attempts + 1

WHEN NOT MATCHED THEN
    INSERT (MessageId, Source, Hash, FirstSeenUtc, LastSeenUtc, Status)
    VALUES (source.MessageId, source.Source, source.Hash, @Now, @Now, 'Seen')

OUTPUT $action, inserted.ProcessedUtc;
```

This ensures that:
1. **First time**: Message is inserted as 'Seen', returns `false` (not processed)
2. **Subsequent times**: Message is updated, returns `true` if already processed
3. **Concurrent access**: Only one thread can "win" the first processing attempt

### Message Broker Integration

Example integration with Azure Service Bus:

```csharp
public class ServiceBusMessageProcessor
{
    private readonly IInbox _inbox;
    private readonly IMessageHandler _handler;

    public async Task ProcessMessageAsync(ServiceBusReceivedMessage message)
    {
        try
        {
            // Extract message details
            var messageId = message.MessageId;
            var source = "ServiceBus";
            var content = message.Body.ToString();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

            // Check for duplicate
            var alreadyProcessed = await _inbox.AlreadyProcessedAsync(messageId, source, hash);
            if (alreadyProcessed)
            {
                // Complete the message to remove from queue
                await message.CompleteAsync();
                return;
            }

            // Process message
            await _inbox.MarkProcessingAsync(messageId);
            await _handler.HandleAsync(content);
            await _inbox.MarkProcessedAsync(messageId);

            // Complete the message
            await message.CompleteAsync();
        }
        catch (Exception ex)
        {
            await _inbox.MarkDeadAsync(message.MessageId);
            
            // Dead letter the message for manual investigation
            await message.DeadLetterAsync("ProcessingError", ex.Message);
        }
    }
}
```

### Monitoring and Cleanup

```csharp
public class InboxMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var connectionString = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<SqlInboxOptions>>().Value.ConnectionString;
                
                // Clean up old processed messages (older than 30 days)
                await CleanupOldMessagesAsync(connectionString, TimeSpan.FromDays(30));
                
                // Alert on stuck processing messages
                await AlertOnStuckMessagesAsync(connectionString, TimeSpan.FromHours(1));
                
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken); // Run every 6 hours
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in inbox maintenance");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
    }
}
```

## 4. Timer Scheduler (One-Time Tasks)

Schedules one-time tasks with precise timing and work queue processing for reliable execution.

### Architecture

**Scheduling**: Creates timer records with future `DueTime`
**Processing**: Background workers claim due timers using work queue pattern
**Execution**: Timer events are dispatched through the outbox pattern

### Database Schema

```sql
CREATE TABLE infra.Timers (
    -- Core scheduling fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    DueTime DATETIMEOFFSET NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CorrelationId NVARCHAR(255) NULL,
    
    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),      -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,               -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,            -- Process ownership identifier
    
    -- Legacy status field (for compatibility)
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    
    -- Auditing
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ProcessedAt DATETIMEOFFSET NULL,
    LastError NVARCHAR(MAX) NULL
);

-- Critical index for efficient timer processing
CREATE INDEX IX_Timers_WorkQueue ON infra.Timers(StatusCode, DueTime) 
    INCLUDE(Id, OwnerToken) WHERE StatusCode = 0;
```

### Basic Usage

```csharp
public class NotificationService
{
    private readonly ISchedulerClient _scheduler;

    public async Task ScheduleReminderAsync(int userId, DateTime reminderTime)
    {
        var payload = JsonSerializer.Serialize(new ReminderPayload 
        { 
            UserId = userId,
            Message = "Don't forget your appointment!",
            EmailAddress = "user@example.com"
        });

        var timerId = await _scheduler.ScheduleTimerAsync(
            topic: "SendReminder",
            payload: payload,
            dueTime: reminderTime);

        _logger.LogInformation("Scheduled reminder {TimerId} for user {UserId} at {DueTime}", 
            timerId, userId, reminderTime);
    }

    public async Task CancelReminderAsync(string timerId)
    {
        var cancelled = await _scheduler.CancelTimerAsync(timerId);
        if (cancelled)
        {
            _logger.LogInformation("Reminder {TimerId} cancelled successfully", timerId);
        }
        else
        {
            _logger.LogWarning("Reminder {TimerId} not found or already processed", timerId);
        }
    }
}
```

### Timer Processing with Work Queue

The platform includes background timer processing:

```csharp
public class TimerProcessingService : BackgroundService
{
    private readonly ISchedulerClient _scheduler;
    private readonly IOutbox _outbox;
    private readonly Guid _ownerToken = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Claim due timers (30-second lease, batch of 20)
                var claimedTimerIds = await _scheduler.ClaimTimersAsync(_ownerToken, 30, 20, stoppingToken);
                
                if (claimedTimerIds.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                var processedIds = new List<Guid>();
                var failedIds = new List<Guid>();

                foreach (var timerId in claimedTimerIds)
                {
                    try
                    {
                        // Get timer details
                        var timer = await GetTimerAsync(timerId);
                        
                        // Dispatch timer event through outbox
                        await _outbox.EnqueueAsync(
                            topic: timer.Topic,
                            payload: timer.Payload,
                            correlationId: timer.CorrelationId);
                            
                        processedIds.Add(timerId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process timer {TimerId}", timerId);
                        failedIds.Add(timerId);
                    }
                }

                // Acknowledge results
                if (processedIds.Count > 0)
                    await _scheduler.AckTimersAsync(_ownerToken, processedIds, stoppingToken);

                if (failedIds.Count > 0)
                    await _scheduler.AbandonTimersAsync(_ownerToken, failedIds, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in timer processing service");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
```

### Timer Claim Logic

The `Timers_Claim` stored procedure implements domain-specific logic:

```sql
CREATE OR ALTER PROCEDURE infra.Timers_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    -- Only claim timers that are due (DueTime <= now)
    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM infra.Timers WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0 /* Ready */
          AND DueTime <= @now /* Due for execution */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY DueTime, CreatedAt
    )
    UPDATE t SET 
        StatusCode = 1 /* InProgress */, 
        OwnerToken = @OwnerToken, 
        LockedUntil = @until
    OUTPUT inserted.Id
    FROM infra.Timers t
    JOIN cte ON cte.Id = t.Id;
END
```

### High-Precision Timing

For scenarios requiring precise timing:

```csharp
public class PrecisionTimerService
{
    private readonly ISchedulerClient _scheduler;
    private readonly TimeProvider _timeProvider;

    public async Task SchedulePreciseTaskAsync(TimeSpan delay)
    {
        // Use database-authoritative time for precision
        var dueTime = _timeProvider.GetUtcNow().Add(delay);
        
        var timerId = await _scheduler.ScheduleTimerAsync(
            topic: "PreciseTask",
            payload: JsonSerializer.Serialize(new { ScheduledAt = dueTime }),
            dueTime: dueTime);
            
        _logger.LogInformation("Scheduled precise task {TimerId} for {DueTime}", timerId, dueTime);
    }
}
```

### Timer Event Handling

Timer events are processed through your outbox message handlers:

```csharp
public class ReminderHandler : IOutboxHandler
{
    private readonly IEmailService _emailService;

    public string Topic => "SendReminder";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var reminder = JsonSerializer.Deserialize<ReminderPayload>(message.Payload);
        
        await _emailService.SendEmailAsync(
            to: reminder.EmailAddress,
            subject: "Reminder",
            body: reminder.Message);
            
        _logger.LogInformation("Sent reminder to user {UserId}", reminder.UserId);
    }
}
```

### Bulk Timer Operations

```csharp
public class BulkTimerService
{
    private readonly ISchedulerClient _scheduler;

    public async Task ScheduleWeeklyRemindersAsync(IEnumerable<User> users)
    {
        var baseTime = DateTime.UtcNow.Date.AddDays(7).AddHours(9); // Next week at 9 AM
        
        var scheduleTasks = users.Select(async (user, index) =>
        {
            // Spread timers over 1 hour to avoid thundering herd
            var dueTime = baseTime.AddMinutes(index % 60);
            
            return await _scheduler.ScheduleTimerAsync(
                topic: "WeeklyReminder",
                payload: JsonSerializer.Serialize(new { UserId = user.Id }),
                dueTime: dueTime);
        });

        var timerIds = await Task.WhenAll(scheduleTasks);
        _logger.LogInformation("Scheduled {Count} weekly reminders", timerIds.Length);
    }
}
```

## 5. Job Scheduler (Recurring Jobs)

Schedules recurring jobs using cron expressions with work queue processing for reliable execution.

### Architecture

**Job Definitions**: Stored in `infra.Jobs` table with cron schedules
**Job Runs**: Individual execution instances stored in `infra.JobRuns` table  
**Scheduling**: Background service creates `JobRuns` based on cron schedules
**Processing**: Workers claim and execute job runs using work queue pattern

### Database Schema

```sql
-- Job definitions with cron schedules
CREATE TABLE infra.Jobs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobName NVARCHAR(100) NOT NULL,
    CronSchedule NVARCHAR(100) NOT NULL,         -- e.g., "0 */5 * * * *"
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,
    
    -- Scheduling state
    NextDueTime DATETIMEOFFSET NULL,
    LastRunTime DATETIMEOFFSET NULL,
    LastRunStatus NVARCHAR(20) NULL
);

-- Individual job execution instances
CREATE TABLE infra.JobRuns (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL REFERENCES infra.Jobs(Id),
    ScheduledTime DATETIMEOFFSET NOT NULL,
    
    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),      -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,               -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,            -- Process ownership identifier
    
    -- Legacy fields for compatibility
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    
    -- Execution tracking
    StartTime DATETIMEOFFSET NULL,
    EndTime DATETIMEOFFSET NULL,
    Output NVARCHAR(MAX) NULL,
    LastError NVARCHAR(MAX) NULL
);

-- Efficient work queue index
CREATE INDEX IX_JobRuns_WorkQueue ON infra.JobRuns(StatusCode, ScheduledTime) 
    INCLUDE(Id, OwnerToken) WHERE StatusCode = 0;
```

### Basic Usage

```csharp
public class MaintenanceService
{
    private readonly ISchedulerClient _scheduler;

    public async Task SetupRecurringJobsAsync()
    {
        // Daily cleanup at 2 AM UTC
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "DailyCleanup",
            topic: "RunCleanup",
            cronSchedule: "0 0 2 * * *", // Every day at 2:00 AM
            payload: JsonSerializer.Serialize(new CleanupConfig 
            { 
                RetentionDays = 90,
                IncludeArchives = true,
                Tables = new[] { "Logs", "TempData", "Sessions" }
            }));

        // Weekly reports every Monday at 9 AM UTC
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "WeeklyReport",
            topic: "GenerateReport",
            cronSchedule: "0 0 9 * * 1", // Every Monday at 9:00 AM
            payload: JsonSerializer.Serialize(new ReportConfig 
            { 
                ReportType = "Weekly",
                Recipients = new[] { "admin@company.com", "reports@company.com" },
                IncludeTrends = true
            }));

        // Health checks every 5 minutes
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "HealthCheck",
            topic: "CheckSystemHealth",
            cronSchedule: "0 */5 * * * *", // Every 5 minutes
            payload: JsonSerializer.Serialize(new HealthCheckConfig
            {
                CheckDatabases = true,
                CheckExternalServices = true,
                TimeoutSeconds = 30
            }));
    }

    public async Task ManageJobsAsync()
    {
        // Disable a job temporarily
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "WeeklyReport",
            topic: "GenerateReport", 
            cronSchedule: "0 0 9 * * 1",
            payload: "...");
        // Note: To disable, you'd need to delete and recreate or use database updates

        // Delete a job and all its pending runs
        await _scheduler.DeleteJobAsync("HealthCheck");

        // Trigger a job immediately (outside normal schedule)
        await _scheduler.TriggerJobAsync("DailyCleanup");
    }
}
```

### Cron Expression Examples

```csharp
// Common cron patterns (using 6-field format: second minute hour day month dayofweek)

"0 */5 * * * *"      // Every 5 minutes
"0 0 * * * *"        // Every hour at minute 0
"0 30 2 * * *"       // Every day at 2:30 AM
"0 0 9 * * 1"        // Every Monday at 9 AM
"0 0 0 1 * *"        // Every first day of the month at midnight
"0 0 18 * * 1-5"     // Every weekday at 6 PM
"0 15 10,14 * * *"   // At 10:15 AM and 2:15 PM every day
"0 0 8-17/2 * * *"   // Every 2 hours from 8 AM to 5 PM
"0 0 0 * * 6,0"      // Every Saturday and Sunday at midnight
```

### Job Run Processing with Work Queue

```csharp
public class JobRunProcessingService : BackgroundService
{
    private readonly ISchedulerClient _scheduler;
    private readonly IOutbox _outbox;
    private readonly Guid _ownerToken = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Claim due job runs (60-second lease, batch of 10)
                var claimedJobRunIds = await _scheduler.ClaimJobRunsAsync(_ownerToken, 60, 10, stoppingToken);
                
                if (claimedJobRunIds.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var processedIds = new List<Guid>();
                var failedIds = new List<Guid>();

                foreach (var jobRunId in claimedJobRunIds)
                {
                    try
                    {
                        // Get job run details with job definition
                        var jobRun = await GetJobRunWithJobAsync(jobRunId);
                        
                        // Record start time
                        await UpdateJobRunStartTimeAsync(jobRunId);
                        
                        // Dispatch job execution through outbox
                        await _outbox.EnqueueAsync(
                            topic: jobRun.Job.Topic,
                            payload: jobRun.Job.Payload ?? "{}",
                            correlationId: $"job-run-{jobRunId}");
                            
                        // Record completion
                        await UpdateJobRunCompletionAsync(jobRunId, "Succeeded", null);
                            
                        processedIds.Add(jobRunId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process job run {JobRunId}", jobRunId);
                        
                        // Record error
                        await UpdateJobRunCompletionAsync(jobRunId, "Failed", ex.Message);
                        
                        failedIds.Add(jobRunId);
                    }
                }

                // Acknowledge results
                if (processedIds.Count > 0)
                    await _scheduler.AckJobRunsAsync(_ownerToken, processedIds, stoppingToken);

                if (failedIds.Count > 0)
                    await _scheduler.AbandonJobRunsAsync(_ownerToken, failedIds, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job run processing service");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
```

### Job Scheduling Service

The platform includes a background service that creates job runs based on cron schedules:

```csharp
public class JobSchedulingService : BackgroundService
{
    private readonly IServiceProvider _services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var schedulerClient = scope.ServiceProvider.GetRequiredService<ISchedulerClient>();
                
                // Get enabled jobs that need scheduling
                var jobsToSchedule = await GetJobsNeedingSchedulingAsync();
                
                foreach (var job in jobsToSchedule)
                {
                    try
                    {
                        // Calculate next run time based on cron schedule
                        var nextRunTime = CalculateNextRunTime(job.CronSchedule, job.LastRunTime);
                        
                        if (nextRunTime <= DateTime.UtcNow)
                        {
                            // Create job run for execution
                            await CreateJobRunAsync(job.Id, nextRunTime);
                            
                            // Update job's last run time and next due time
                            await UpdateJobSchedulingInfoAsync(job.Id, nextRunTime, 
                                CalculateNextRunTime(job.CronSchedule, nextRunTime));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to schedule job {JobName}", job.JobName);
                    }
                }
                
                // Run every 30 seconds to catch due jobs
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job scheduling service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
```

### Job Run Claim Logic

The `JobRuns_Claim` stored procedure implements time-based claiming:

```sql
CREATE OR ALTER PROCEDURE infra.JobRuns_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    -- Only claim job runs that are due (ScheduledTime <= now)
    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM infra.JobRuns WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0 /* Ready */
          AND ScheduledTime <= @now /* Due for execution */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY ScheduledTime, Id
    )
    UPDATE jr SET 
        StatusCode = 1 /* InProgress */, 
        OwnerToken = @OwnerToken, 
        LockedUntil = @until
    OUTPUT inserted.Id
    FROM infra.JobRuns jr
    JOIN cte ON cte.Id = jr.Id;
END
```

### Job Event Handling

Job executions are processed through outbox message handlers:

```csharp
public class CleanupJobHandler : IOutboxHandler
{
    private readonly IDataCleanupService _cleanupService;
    private readonly ILogger<CleanupJobHandler> _logger;

    public string Topic => "RunCleanup";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<CleanupConfig>(message.Payload);
        
        _logger.LogInformation("Starting cleanup job with retention {Days} days", config.RetentionDays);
        
        var startTime = DateTime.UtcNow;
        var results = new CleanupResults();

        try
        {
            // Perform cleanup operations
            results.DeletedLogs = await _cleanupService.CleanupLogsAsync(
                config.RetentionDays, cancellationToken);
                
            results.DeletedTempData = await _cleanupService.CleanupTempDataAsync(
                config.RetentionDays, cancellationToken);
                
            if (config.IncludeArchives)
            {
                results.ArchivedFiles = await _cleanupService.ArchiveOldFilesAsync(
                    config.RetentionDays, cancellationToken);
            }
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Cleanup completed in {Duration}. Results: {@Results}", 
                duration, results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup job failed after {Duration}", 
                DateTime.UtcNow - startTime);
            throw;
        }
    }
}

public class ReportGenerationHandler : IOutboxHandler
{
    private readonly IReportService _reportService;
    private readonly IEmailService _emailService;

    public string Topic => "GenerateReport";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<ReportConfig>(message.Payload);
        
        // Generate report
        var report = await _reportService.GenerateReportAsync(config.ReportType, 
            includeTrends: config.IncludeTrends, cancellationToken);
        
        // Send to recipients
        foreach (var recipient in config.Recipients)
        {
            await _emailService.SendReportAsync(recipient, report, cancellationToken);
        }
        
        _logger.LogInformation("Generated and sent {ReportType} report to {Count} recipients", 
            config.ReportType, config.Recipients.Length);
    }
}
```

### Advanced Job Management

```csharp
public class JobManagementService
{
    private readonly ISchedulerClient _scheduler;

    public async Task CreateDynamicBackupJobAsync(string databaseName, TimeZoneInfo timeZone)
    {
        // Create job with timezone-adjusted schedule
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var backupHour = 3; // 3 AM local time
        
        // Convert to UTC cron expression
        var utcHour = TimeZoneInfo.ConvertTimeToUtc(
            localTime.Date.AddHours(backupHour), timeZone).Hour;
            
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: $"Backup_{databaseName}",
            topic: "BackupDatabase",
            cronSchedule: $"0 0 {utcHour} * * *", // Daily at calculated UTC hour
            payload: JsonSerializer.Serialize(new BackupConfig 
            { 
                DatabaseName = databaseName,
                RetentionDays = 30,
                CompressionLevel = "High",
                NotifyOnCompletion = true
            }));
    }

    public async Task CreateConditionalJobAsync()
    {
        // Job that only runs on business days
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "BusinessDayReport",
            topic: "GenerateBusinessReport",
            cronSchedule: "0 0 8 * * 1-5", // Monday through Friday at 8 AM
            payload: JsonSerializer.Serialize(new BusinessReportConfig
            {
                ExcludeHolidays = true,
                IncludeWeekendData = false
            }));
    }

## 6. Generic Fan-Out on a Schedule

The Fan-Out system provides a first-class, generic capability for running planners on a schedule, computing which units of work are due, and enqueuing per-unit work items for processing. It reuses existing primitives (Jobs, Outbox, Inbox, Locks) and supports multiple independent fan-outs with custom planners and dispatchers.

### Architecture

The fan-out system uses a **four-phase pipeline**:

1. **Planning Phase**: `IFanoutPlanner` determines which slices are due for processing
2. **Coordination Phase**: `IFanoutCoordinator` acquires a lease and orchestrates the process  
3. **Dispatch Phase**: `IFanoutDispatcher` enqueues slices via Outbox for downstream processing
4. **Processing Phase**: Outbox handlers consume slices and update completion cursors

### Core Components

- **`FanoutSlice`**: A unit of work identified by `(FanoutTopic, ShardKey, WorkKey, WindowStart)`
- **`IFanoutPlanner`**: Application-specific logic for determining which work is due
- **`IFanoutCoordinator`**: Orchestrates fanout using leases for singleton behavior
- **`IFanoutDispatcher`**: Enqueues slices to Outbox with topic `fanout:{topic}:{workKey}`
- **`FanoutPolicy`**: Stores cadence settings per topic/work key combination
- **`FanoutCursor`**: Tracks last completion timestamps per slice for resumable processing

### Basic Usage

```csharp
// Program.cs - Setup fanout system
builder.Services.AddSqlFanout(connectionString)
    .AddFanoutTopic<EtlFanoutPlanner>(new FanoutTopicOptions
    {
        FanoutTopic = "etl",
        WorkKey = "payments",
        Cron = "*/10 * * * *",        // Every 10 minutes
        DefaultEverySeconds = 600,
        JitterSeconds = 60
    })
    .AddFanoutTopic<EtlFanoutPlanner>(new FanoutTopicOptions
    {
        FanoutTopic = "etl", 
        WorkKey = "vendors",
        Cron = "0 */4 * * * *",       // Every 4 hours
        DefaultEverySeconds = 14400,
        JitterSeconds = 300
    });

// Register handlers for processing fanout slices
builder.Services.AddOutboxHandler<PaymentEtlHandler>();
builder.Services.AddOutboxHandler<VendorEtlHandler>();
```

### Example Planner Implementation

```csharp
public class EtlFanoutPlanner : BaseFanoutPlanner
{
    private readonly ITenantReader tenantReader;

    public EtlFanoutPlanner(
        IFanoutPolicyRepository policyRepository,
        IFanoutCursorRepository cursorRepository, 
        TimeProvider timeProvider,
        ITenantReader tenantReader)
        : base(policyRepository, cursorRepository, timeProvider)
    {
        this.tenantReader = tenantReader;
    }

    protected override async IAsyncEnumerable<(string ShardKey, string WorkKey)> EnumerateCandidatesAsync(
        string fanoutTopic, string? workKey, CancellationToken ct)
    {
        // Get all enabled tenants
        var tenants = await tenantReader.ListEnabledAsync(ct);
        
        // Define available work types
        var workKeys = workKey is null 
            ? new[] { "payments", "vendors", "contacts", "products" }
            : new[] { workKey };
        
        // Generate all combinations
        foreach (var tenant in tenants)
        foreach (var wk in workKeys)
            yield return (ShardKey: tenant.Id.ToString("D"), WorkKey: wk);
    }
}
```

### Example Outbox Handler

```csharp
public class PaymentEtlHandler : IOutboxHandler
{
    private readonly IInbox inbox;
    private readonly IFanoutCursorRepository cursorRepository;
    private readonly IPaymentEtlService etlService;
    private readonly TimeProvider timeProvider;

    public string Topic => "fanout:etl:payments";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Deserialize fanout slice
        var slice = JsonSerializer.Deserialize<FanoutSlice>(message.Payload)!;
        
        // Create idempotency key
        var idempotencyKey = $"{slice.FanoutTopic}|{slice.WorkKey}|{slice.ShardKey}|{slice.WindowStart:O}";
        
        // Check if already processed
        if (await inbox.AlreadyProcessedAsync(idempotencyKey, "fanout", cancellationToken: cancellationToken))
            return;
        
        // Mark as processing
        await inbox.MarkProcessingAsync(idempotencyKey, cancellationToken);
        
        try
        {
            // Determine processing window
            var since = slice.WindowStart ?? timeProvider.GetUtcNow().AddHours(-1);
            var until = timeProvider.GetUtcNow();
            
            // Perform ETL operation
            await etlService.ProcessPaymentsAsync(
                tenantId: Guid.Parse(slice.ShardKey),
                since: since,
                until: until,
                cancellationToken: cancellationToken);
            
            // Update completion cursor
            await cursorRepository.MarkCompletedAsync(
                slice.FanoutTopic, slice.WorkKey, slice.ShardKey, until, cancellationToken);
            
            // Mark as processed
            await inbox.MarkProcessedAsync(idempotencyKey, cancellationToken);
        }
        catch
        {
            await inbox.MarkDeadAsync(idempotencyKey, cancellationToken);
            throw;
        }
    }
}
```

### Database Schema

The fanout system uses two tables:

```sql
-- Stores cadence policies per topic/work key
CREATE TABLE infra.FanoutPolicy (
    FanoutTopic NVARCHAR(100) NOT NULL,
    WorkKey NVARCHAR(100) NOT NULL,
    DefaultEverySeconds INT NOT NULL,
    JitterSeconds INT NOT NULL DEFAULT 60,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT PK_FanoutPolicy PRIMARY KEY (FanoutTopic, WorkKey)
);

-- Tracks completion progress per slice
CREATE TABLE infra.FanoutCursor (
    FanoutTopic NVARCHAR(100) NOT NULL,
    WorkKey NVARCHAR(100) NOT NULL, 
    ShardKey NVARCHAR(256) NOT NULL,
    LastCompletedAt DATETIMEOFFSET NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT PK_FanoutCursor PRIMARY KEY (FanoutTopic, WorkKey, ShardKey)
);
```

### Key Features

**Generic Design**: No hard-coded tenant/dataset concepts - uses opaque `ShardKey` and `WorkKey` strings for maximum flexibility.

**Multiple Fan-Outs**: Register many independent fanout topics, each with its own schedule and planner logic.

**Resumable Processing**: `FanoutCursor` tracks completion timestamps, enabling incremental and resumable processing windows.

**Lease Coordination**: Only one instance coordinates each fanout topic/work key combination at a time using distributed leases.

**Outbox Integration**: Leverages existing Outbox infrastructure for reliable delivery, retry, and error handling.

**Inbox Idempotency**: Optional integration with Inbox service prevents duplicate processing of slices.

**Automatic Schema**: Database tables are created automatically with `EnableSchemaDeployment = true`.

### Advanced Configuration

```csharp
// Custom planner with application-specific dependencies
builder.Services.AddScoped<ITenantReader, SqlTenantReader>();
builder.Services.AddScoped<IPaymentEtlService, PaymentEtlService>();

// Multiple work keys for same topic
builder.Services.AddFanoutTopic<EtlFanoutPlanner>(new FanoutTopicOptions
{
    FanoutTopic = "etl",
    WorkKey = "payments",
    Cron = "*/10 * * * *"
})
.AddFanoutTopic<EtlFanoutPlanner>(new FanoutTopicOptions
{
    FanoutTopic = "etl", 
    WorkKey = "vendors",
    Cron = "0 */4 * * * *"
})
.AddFanoutTopic<ReportFanoutPlanner>(new FanoutTopicOptions
{
    FanoutTopic = "reports",
    Cron = "0 0 6 * * *",     // Daily at 6 AM
    DefaultEverySeconds = 86400,
    JitterSeconds = 1800      // 30 minute jitter
});

// Separate schema for fanout tables
builder.Services.AddSqlFanout(new SqlFanoutOptions
{
    ConnectionString = connectionString,
    SchemaName = "fanout",
    PolicyTableName = "Policy", 
    CursorTableName = "Cursor"
});
```

The fanout system provides a powerful abstraction for periodic, distributed processing that scales with your application's sharding strategy while maintaining simple setup and reliable execution semantics.

## Configuration

### Complete Configuration Example

```csharp
// appsettings.json
{
  "SqlScheduler": {
    "ConnectionString": "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    "SchemaName": "infra",
    "EnableSchemaDeployment": true,
    "MaxPollingInterval": "00:00:30",
    "EnableBackgroundWorkers": true
  },
  "Logging": {
    "LogLevel": {
      "Bravellian.Platform": "Information"
    }
  }
}

// Program.cs - Comprehensive Setup
var builder = WebApplication.CreateBuilder(args);

// Option 1: Complete platform with configuration binding
builder.Services.AddSqlScheduler(builder.Configuration.GetSection("SqlScheduler"));

// Option 2: Explicit configuration with all options
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")!,
    SchemaName = "infra",
    EnableSchemaDeployment = true,      // Auto-create tables and procedures
    MaxPollingInterval = TimeSpan.FromSeconds(30),
    EnableBackgroundWorkers = true      // Start polling services
});

// Option 3: Individual components for fine-grained control
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")!,
    SchemaName = "infra",
    TableName = "Outbox",
    EnableSchemaDeployment = true
});

builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")!,
    SchemaName = "infra", 
    TableName = "Inbox",
    EnableSchemaDeployment = true
});

builder.Services.AddSystemLeases(new SystemLeaseOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")!,
    SchemaName = "infra"
});

// Register custom message handlers
builder.Services.AddTransient<IOutboxHandler, OrderEventHandler>();
builder.Services.AddTransient<IOutboxHandler, NotificationHandler>();
builder.Services.AddTransient<IOutboxHandler, CleanupJobHandler>();

// Add health checks with custom tags
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck("scheduler", tags: new[] { "database", "scheduler" })
    .AddSqlServer(builder.Configuration.GetConnectionString("Default")!, 
        name: "database", tags: new[] { "database" });

var app = builder.Build();

// Configure health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("database")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("scheduler")
});

app.Run();
```

### Configuration Options Reference

#### SqlSchedulerOptions
```csharp
public class SqlSchedulerOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "infra";
    public bool EnableSchemaDeployment { get; set; } = false;
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableBackgroundWorkers { get; set; } = true;
}
```

#### SqlOutboxOptions
```csharp
public class SqlOutboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "infra";
    public string TableName { get; set; } = "Outbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

#### SqlInboxOptions
```csharp
public class SqlInboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "infra";
    public string TableName { get; set; } = "Inbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

#### SystemLeaseOptions
```csharp
public class SystemLeaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "infra";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

### Environment-Specific Configuration

```csharp
// Development environment
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSqlScheduler(new SqlSchedulerOptions
    {
        ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=DevApp;Integrated Security=true;",
        EnableSchemaDeployment = true,          // Auto-create in dev
        MaxPollingInterval = TimeSpan.FromSeconds(5),  // Fast polling for dev
        EnableBackgroundWorkers = true
    });
}

// Production environment  
if (builder.Environment.IsProduction())
{
    builder.Services.AddSqlScheduler(new SqlSchedulerOptions
    {
        ConnectionString = builder.Configuration.GetConnectionString("Production")!,
        EnableSchemaDeployment = false,         // Manual schema management
        MaxPollingInterval = TimeSpan.FromSeconds(30), // Conservative polling
        EnableBackgroundWorkers = true
    });
}
```

### Docker and Container Configuration

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MyApp.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish  
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

```yaml
# docker-compose.yml
version: '3.8'
services:
  app:
    build: .
    environment:
      - ConnectionStrings__Default=Server=sqlserver;Database=MyApp;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true;
      - SqlScheduler__EnableSchemaDeployment=true
      - SqlScheduler__MaxPollingInterval=00:00:10
    depends_on:
      - sqlserver
    
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourPassword123!
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql

volumes:
  sqlserver_data:
```

## Database Schema Reference

The platform creates a comprehensive set of tables and stored procedures for reliable work queue processing.

### Core Tables

#### Outbox Table
```sql
CREATE TABLE infra.Outbox (
    -- Core message fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Work queue state management
    Status TINYINT NOT NULL DEFAULT(0),           -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,                -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,             -- Process ownership identifier
    
    -- Processing metadata
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL,
    
    -- Error handling and retry
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Message tracking
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CorrelationId NVARCHAR(255) NULL
);

-- Work queue optimization index
CREATE INDEX IX_Outbox_WorkQueue ON infra.Outbox(Status, CreatedAt) 
    INCLUDE(Id, OwnerToken);

-- Legacy processing index
CREATE INDEX IX_Outbox_GetNext ON infra.Outbox(IsProcessed, NextAttemptAt)
    INCLUDE(Id, Payload, Topic, RetryCount) 
    WHERE IsProcessed = 0;
```

#### Timers Table
```sql
CREATE TABLE infra.Timers (
    -- Core scheduling fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    DueTime DATETIMEOFFSET NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CorrelationId NVARCHAR(255) NULL,
    
    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),       -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,                -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,             -- Process ownership identifier
    
    -- Legacy status management
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    
    -- Auditing
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ProcessedAt DATETIMEOFFSET NULL,
    LastError NVARCHAR(MAX) NULL
);

-- Efficient timer lookup index
CREATE INDEX IX_Timers_WorkQueue ON infra.Timers(StatusCode, DueTime) 
    INCLUDE(Id, OwnerToken) WHERE StatusCode = 0;

-- Legacy index
CREATE INDEX IX_Timers_GetNext ON infra.Timers(Status, DueTime)
    INCLUDE(Id, Topic) WHERE Status = 'Pending';
```

#### Jobs Table
```sql
CREATE TABLE infra.Jobs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobName NVARCHAR(100) NOT NULL,
    CronSchedule NVARCHAR(100) NOT NULL,          -- e.g., "0 */5 * * * *"
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,
    
    -- Scheduling state tracking
    NextDueTime DATETIMEOFFSET NULL,
    LastRunTime DATETIMEOFFSET NULL,
    LastRunStatus NVARCHAR(20) NULL
);

-- Ensure unique job names
CREATE UNIQUE INDEX UQ_Jobs_JobName ON infra.Jobs(JobName);
```

#### JobRuns Table
```sql
CREATE TABLE infra.JobRuns (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL REFERENCES infra.Jobs(Id),
    ScheduledTime DATETIMEOFFSET NOT NULL,
    
    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),       -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,                -- UTC lease expiration time
    OwnerToken UNIQUEIDENTIFIER NULL,             -- Process ownership identifier
    
    -- Legacy status management
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    
    -- Execution tracking
    StartTime DATETIMEOFFSET NULL,
    EndTime DATETIMEOFFSET NULL,
    Output NVARCHAR(MAX) NULL,
    LastError NVARCHAR(MAX) NULL
);

-- Work queue processing index
CREATE INDEX IX_JobRuns_WorkQueue ON infra.JobRuns(StatusCode, ScheduledTime) 
    INCLUDE(Id, OwnerToken) WHERE StatusCode = 0;

-- Legacy index
CREATE INDEX IX_JobRuns_GetNext ON infra.JobRuns(Status, ScheduledTime)
    WHERE Status = 'Pending';
```

#### Inbox Table
```sql
CREATE TABLE infra.Inbox (
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,                         -- Optional content verification
    FirstSeenUtc DATETIME2(3) NOT NULL,
    LastSeenUtc DATETIME2(3) NOT NULL,
    ProcessedUtc DATETIME2(3) NULL,
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'    -- Seen, Processing, Done, Dead
);

-- Processing optimization index
CREATE INDEX IX_Inbox_Processing ON infra.Inbox(Status, LastSeenUtc)
    WHERE Status IN ('Seen', 'Processing');
```

#### Lease Table (Lease System v2)
```sql
CREATE TABLE infra.Lease (
    LeaseName NVARCHAR(200) NOT NULL PRIMARY KEY,
    Owner NVARCHAR(200) NOT NULL,
    LeaseUntilUtc DATETIME2(3) NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    RenewedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Index for expired lease cleanup
CREATE INDEX IX_Lease_Expired ON infra.Lease(LeaseUntilUtc)
    WHERE LeaseUntilUtc < SYSUTCDATETIME();
```

### Stored Procedures

The platform generates work queue stored procedures following a consistent pattern:

#### Outbox Procedures
```sql
-- Atomically claim ready outbox messages
infra.Outbox_Claim (@OwnerToken, @LeaseSeconds, @BatchSize)

-- Mark messages as successfully processed
infra.Outbox_Ack (@OwnerToken, @Ids)

-- Return messages to ready state for retry
infra.Outbox_Abandon (@OwnerToken, @Ids)

-- Mark messages as failed
infra.Outbox_Fail (@OwnerToken, @Ids)

-- Recover expired leases
infra.Outbox_ReapExpired ()
```

#### Timer Procedures
```sql
-- Claim due timers for processing
infra.Timers_Claim (@OwnerToken, @LeaseSeconds, @BatchSize)

-- Acknowledge completed timers
infra.Timers_Ack (@OwnerToken, @Ids)

-- Abandon failed timers for retry
infra.Timers_Abandon (@OwnerToken, @Ids)

-- Reap expired timer leases
infra.Timers_ReapExpired ()
```

#### JobRuns Procedures
```sql
-- Claim due job runs for processing
infra.JobRuns_Claim (@OwnerToken, @LeaseSeconds, @BatchSize)

-- Acknowledge completed job runs
infra.JobRuns_Ack (@OwnerToken, @Ids)

-- Abandon failed job runs for retry
infra.JobRuns_Abandon (@OwnerToken, @Ids)

-- Reap expired job run leases
infra.JobRuns_ReapExpired ()
```

### User-Defined Table Types

For efficient batch operations:

```sql
-- Used for passing multiple IDs to stored procedures
CREATE TYPE infra.GuidIdList AS TABLE
(
    Id UNIQUEIDENTIFIER NOT NULL
);
```

### Schema Deployment

The platform supports automatic schema deployment:

```csharp
// Automatic deployment during startup
services.AddSqlScheduler(new SqlSchedulerOptions 
{ 
    EnableSchemaDeployment = true 
});

// Manual deployment
await DatabaseSchemaManager.EnsureOutboxSchemaAsync(connectionString);
await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(connectionString);
await DatabaseSchemaManager.EnsureInboxSchemaAsync(connectionString);
await DatabaseSchemaManager.EnsureLeaseSchemaAsync(connectionString);
```

### Database Permissions

Required SQL Server permissions for the application user:

```sql
-- Core permissions for work queue operations
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Outbox TO [AppUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Timers TO [AppUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Jobs TO [AppUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.JobRuns TO [AppUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Inbox TO [AppUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Lease TO [AppUser];

-- Stored procedure execution
GRANT EXECUTE ON infra.Outbox_Claim TO [AppUser];
GRANT EXECUTE ON infra.Outbox_Ack TO [AppUser];
GRANT EXECUTE ON infra.Outbox_Abandon TO [AppUser];
GRANT EXECUTE ON infra.Outbox_Fail TO [AppUser];
GRANT EXECUTE ON infra.Outbox_ReapExpired TO [AppUser];

-- Similar grants for Timers and JobRuns procedures...

-- User-defined table type permissions
GRANT EXECUTE ON TYPE::infra.GuidIdList TO [AppUser];

-- For schema deployment (if enabled)
GRANT ALTER ON SCHEMA::infra TO [AppUser];  -- Only if auto-deployment enabled
```

## Operational Considerations

### Production Deployment

#### Database Considerations

**Connection Pooling**: Configure appropriate connection pool settings:
```csharp
var connectionString = "Server=prod-sql;Database=MyApp;Integrated Security=true;" +
                      "Min Pool Size=5;Max Pool Size=100;Pooling=true;" +
                      "Connection Timeout=30;Command Timeout=60;";
```

**Scaling Strategy**: The platform is designed for horizontal scaling:
- Multiple application instances can run simultaneously
- Work queue operations use atomic claims to prevent conflicts
- Database becomes the coordination point for distributed processing

**High Availability**: Configure SQL Server for high availability:
- SQL Server Always On Availability Groups
- Automatic failover support
- Connection string with failover partner

#### Monitoring and Observability

**Health Checks**: Monitor system health across all components:
```csharp
// Comprehensive health check setup
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        });
        await context.Response.WriteAsync(result);
    }
});
```

**Key Metrics to Track**:
- Outbox processing rate and latency
- Timer/job execution accuracy and delays
- Lease acquisition success rates
- Background service health status
- Database connection pool metrics

**Logging Configuration**:
```csharp
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Bravellian.Platform.OutboxPollingService": "Warning",
      "Bravellian.Platform.LeaseRunner": "Information",
      "System.Net.Http": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

#### Performance Tuning

**Polling Intervals**: Adjust based on throughput requirements:
```csharp
// High throughput environment
services.AddSqlScheduler(new SqlSchedulerOptions
{
    MaxPollingInterval = TimeSpan.FromSeconds(5)  // More frequent polling
});

// Low throughput environment  
services.AddSqlScheduler(new SqlSchedulerOptions
{
    MaxPollingInterval = TimeSpan.FromMinutes(1)  // Less frequent polling
});
```

**Batch Sizes**: Optimize for your workload:
```csharp
// In your custom processors
var claimedIds = await outbox.ClaimAsync(ownerToken, 
    leaseSeconds: 60,      // Longer lease for complex processing
    batchSize: 100);       // Larger batches for efficiency
```

**Database Maintenance**: Regular maintenance tasks:
```sql
-- Clean up old processed outbox messages (run weekly)
DELETE FROM infra.Outbox 
WHERE Status = 2 /* Done */ 
  AND ProcessedAt < DATEADD(day, -30, GETUTCDATE());

-- Clean up old completed job runs (run monthly)
DELETE FROM infra.JobRuns 
WHERE StatusCode = 2 /* Done */ 
  AND EndTime < DATEADD(day, -90, GETUTCDATE());

-- Update statistics for optimal query performance
UPDATE STATISTICS infra.Outbox;
UPDATE STATISTICS infra.Timers;
UPDATE STATISTICS infra.JobRuns;

-- Rebuild fragmented indexes if needed
ALTER INDEX IX_Outbox_WorkQueue ON infra.Outbox REBUILD;
```

### Error Handling and Resilience

#### Retry Strategies

**Exponential Backoff**: Built into outbox processing:
```csharp
public static TimeSpan CalculateBackoffDelay(int attempt)
{
    // 1s, 2s, 4s, 8s, 16s, 32s, then 60s max
    var seconds = Math.Min(Math.Pow(2, attempt), 60);
    return TimeSpan.FromSeconds(seconds);
}
```

**Circuit Breaker Pattern**: For external service calls:
```csharp
public class ResilientMessageHandler : IOutboxHandler
{
    private readonly HttpClient _httpClient;
    private readonly CircuitBreakerPolicy _circuitBreaker;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await _circuitBreaker.ExecuteAsync(async () =>
        {
            // Call external service with circuit breaker protection
            await _httpClient.PostAsync("/api/webhook", 
                new StringContent(message.Payload), cancellationToken);
        });
    }
}
```

#### Dead Letter Handling

**Poison Message Detection**:
```csharp
public class PoisonMessageHandler
{
    private const int MaxRetryAttempts = 5;

    public async Task ProcessMessageAsync(OutboxMessage message)
    {
        if (message.RetryCount >= MaxRetryAttempts)
        {
            // Move to dead letter storage
            await _deadLetterService.StoreAsync(message);
            
            // Mark as failed to stop retries
            await _outbox.FailAsync(_ownerToken, new[] { message.Id });
            
            // Alert operations team
            await _alertingService.SendAlertAsync($"Poison message detected: {message.Id}");
            return;
        }

        // Normal processing...
    }
}
```

#### Database Connection Resilience

```csharp
// Configure retry policy for transient failures
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = connectionString
});

// Custom retry policy for database operations
services.AddTransient<IRetryPolicy>(provider => 
    Policy.Handle<SqlException>(ex => IsTransientError(ex))
          .WaitAndRetryAsync(
              retryCount: 3,
              sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
              onRetry: (outcome, timespan, retryCount, context) =>
              {
                  var logger = provider.GetRequiredService<ILogger<RetryPolicy>>();
                  logger.LogWarning("Retry {RetryCount} for database operation after {Delay}ms", 
                      retryCount, timespan.TotalMilliseconds);
              }));

private static bool IsTransientError(SqlException ex)
{
    // Common transient error codes
    var transientErrors = new[] { 2, 53, 121, 233, 10053, 10054, 10060, 40197, 40501, 40613 };
    return transientErrors.Contains(ex.Number);
}
```

### Security Considerations

#### Connection String Security

```csharp
// Use managed identity in Azure
var connectionString = "Server=myserver.database.windows.net;Database=MyApp;" +
                      "Authentication=Active Directory Managed Identity;";

// Use connection string encryption
services.Configure<SqlSchedulerOptions>(options =>
{
    options.ConnectionString = _configuration.GetConnectionString("Encrypted");
});
```

#### SQL Injection Prevention

The platform uses parameterized queries throughout:
```sql
-- All stored procedures use proper parameterization
CREATE PROCEDURE infra.Outbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT
AS
BEGIN
    -- Safe parameterized SQL - no injection risk
    UPDATE o SET OwnerToken = @OwnerToken
    WHERE Status = 0 AND Id IN (
        SELECT TOP (@BatchSize) Id FROM infra.Outbox WHERE Status = 0
    );
END
```

#### Least Privilege Database Access

```sql
-- Create dedicated application user
CREATE USER [AppSchedulerUser] FOR LOGIN [AppSchedulerLogin];

-- Grant only necessary permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON infra.Outbox TO [AppSchedulerUser];
GRANT EXECUTE ON infra.Outbox_Claim TO [AppSchedulerUser];
GRANT EXECUTE ON infra.Outbox_Ack TO [AppSchedulerUser];
-- ... other specific procedure grants

-- Deny dangerous permissions
DENY ALTER ON SCHEMA::infra TO [AppSchedulerUser];
DENY DROP ON SCHEMA::infra TO [AppSchedulerUser];
```

### Troubleshooting Guide

#### Common Issues and Solutions

**1. Messages Not Being Processed**
```csharp
// Check if background workers are enabled
services.AddSqlScheduler(new SqlSchedulerOptions
{
    EnableBackgroundWorkers = true  // Ensure this is true
});

// Verify database connectivity
var healthChecks = app.Services.GetRequiredService<HealthCheckService>();
var result = await healthChecks.CheckHealthAsync();
```

**2. High Database CPU Usage**
```sql
-- Check for missing indexes
SELECT DISTINCT 
    CONVERT(decimal(18,2), user_seeks * avg_total_user_cost * (avg_user_impact * 0.01)) AS [index_advantage],
    migs.last_user_seek, 
    mid.[statement] AS [Database.Schema.Table],
    mid.equality_columns, 
    mid.inequality_columns,
    mid.included_columns
FROM sys.dm_db_missing_index_group_stats AS migs
INNER JOIN sys.dm_db_missing_index_groups AS mig ON migs.group_handle = mig.index_group_handle
INNER JOIN sys.dm_db_missing_index_details AS mid ON mig.index_handle = mid.index_handle
ORDER BY index_advantage DESC;
```

**3. Lease Renewal Failures**
```csharp
// Increase lease duration for slow operations
var runner = await LeaseRunner.AcquireAsync(
    leaseApi, clock, timeProvider,
    "long-running-process", "owner",
    leaseDuration: TimeSpan.FromMinutes(10), // Longer lease
    renewPercent: 0.5); // Renew at 50% for safety margin
```

**4. Timer/Job Execution Delays**
```sql
-- Check for lease contention
SELECT 
    COUNT(*) as LeaseCount,
    AVG(DATEDIFF(second, ClaimedAt, GETUTCDATE())) as AvgLeaseAge
FROM infra.Timers 
WHERE Status = 'InProgress' 
  AND LockedUntil > GETUTCDATE();

-- Look for stuck timers
SELECT TOP 10 *
FROM infra.Timers
WHERE Status = 'InProgress'
  AND LockedUntil < GETUTCDATE()
  AND ClaimedAt < DATEADD(minute, -5, GETUTCDATE());
```

#### Diagnostic Queries

```sql
-- Outbox processing statistics
SELECT 
    Topic,
    COUNT(*) as TotalMessages,
    SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) as Ready,
    SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) as InProgress,
    SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) as Done,
    SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) as Failed,
    AVG(RetryCount) as AvgRetries
FROM infra.Outbox
GROUP BY Topic
ORDER BY TotalMessages DESC;

-- Timer execution performance
SELECT 
    DATEPART(hour, DueTime) as Hour,
    COUNT(*) as ScheduledCount,
    AVG(DATEDIFF(second, DueTime, ProcessedAt)) as AvgDelaySeconds,
    MAX(DATEDIFF(second, DueTime, ProcessedAt)) as MaxDelaySeconds
FROM infra.Timers 
WHERE ProcessedAt IS NOT NULL
GROUP BY DATEPART(hour, DueTime)
ORDER BY Hour;

-- Active leases summary
SELECT 
    'Outbox' as TableName,
    COUNT(*) as ActiveLeases,
    MIN(LockedUntil) as EarliestExpiry,
    MAX(LockedUntil) as LatestExpiry
FROM infra.Outbox WHERE Status = 1 AND LockedUntil > GETUTCDATE()
UNION ALL
SELECT 
    'Timers',
    COUNT(*),
    MIN(LockedUntil),
    MAX(LockedUntil)
FROM infra.Timers WHERE StatusCode = 1 AND LockedUntil > GETUTCDATE()
UNION ALL
SELECT 
    'JobRuns',
    COUNT(*),
    MIN(LockedUntil),
    MAX(LockedUntil)
FROM infra.JobRuns WHERE StatusCode = 1 AND LockedUntil > GETUTCDATE();
```

## Testing

The platform includes comprehensive testing utilities for integration testing with SQL Server using Testcontainers.

### Test Base Classes

```csharp
public abstract class SqlServerTestBase : IAsyncLifetime
{
    private MsSqlContainer? _container;
    protected string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourPassword123!")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        
        // Deploy schemas automatically
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString);
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
```

### Outbox Integration Tests

```csharp
public class OutboxIntegrationTests : SqlServerTestBase
{
    [Fact]
    public async Task OutboxService_Should_EnqueueAndRetrieveMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlOutbox(new SqlOutboxOptions 
        { 
            ConnectionString = ConnectionString,
            EnableSchemaDeployment = false // Already deployed in base class
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act - Enqueue message
        await outbox.EnqueueAsync("TestTopic", "TestPayload", "correlation-123");

        // Act - Claim message
        var ownerToken = Guid.NewGuid();
        var claimedIds = await outbox.ClaimAsync(ownerToken, 30, 10);

        // Assert
        claimedIds.Should().HaveCount(1);
        
        // Act - Acknowledge message
        await outbox.AckAsync(ownerToken, claimedIds);
        
        // Verify no more messages available
        var noClaims = await outbox.ClaimAsync(Guid.NewGuid(), 30, 10);
        noClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task OutboxService_Should_HandleTransactionalEnqueue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlOutbox(new SqlOutboxOptions { ConnectionString = ConnectionString });
        var serviceProvider = services.BuildServiceProvider();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act - Enqueue within transaction that rolls back
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        
        await outbox.EnqueueAsync("TestTopic", "TestPayload", transaction, "correlation-456");
        await transaction.RollbackAsync();

        // Assert - Message should not exist
        var claimedIds = await outbox.ClaimAsync(Guid.NewGuid(), 30, 10);
        claimedIds.Should().BeEmpty();
    }
}
```

### Timer Scheduler Tests

```csharp
public class TimerSchedulerTests : SqlServerTestBase
{
    [Fact]
    public async Task SchedulerClient_Should_ScheduleAndClaimTimers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlScheduler(new SqlSchedulerOptions 
        { 
            ConnectionString = ConnectionString,
            EnableSchemaDeployment = false,
            EnableBackgroundWorkers = false // Disable for testing
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var scheduler = serviceProvider.GetRequiredService<ISchedulerClient>();

        // Act - Schedule timer for immediate execution
        var dueTime = DateTimeOffset.UtcNow.AddMilliseconds(-100); // Past due
        var timerId = await scheduler.ScheduleTimerAsync("TestTopic", "TestPayload", dueTime);

        // Act - Claim due timers
        var ownerToken = Guid.NewGuid();
        var claimedTimerIds = await scheduler.ClaimTimersAsync(ownerToken, 30, 10);

        // Assert
        claimedTimerIds.Should().HaveCount(1);
        
        // Act - Acknowledge timer
        await scheduler.AckTimersAsync(ownerToken, claimedTimerIds);
    }

    [Fact]
    public async Task SchedulerClient_Should_CancelPendingTimers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlScheduler(new SqlSchedulerOptions { ConnectionString = ConnectionString });
        var serviceProvider = services.BuildServiceProvider();
        var scheduler = serviceProvider.GetRequiredService<ISchedulerClient>();

        // Act - Schedule future timer
        var dueTime = DateTimeOffset.UtcNow.AddHours(1);
        var timerId = await scheduler.ScheduleTimerAsync("TestTopic", "TestPayload", dueTime);

        // Act - Cancel timer
        var cancelled = await scheduler.CancelTimerAsync(timerId);

        // Assert
        cancelled.Should().BeTrue();
        
        // Verify timer cannot be claimed
        var claimedIds = await scheduler.ClaimTimersAsync(Guid.NewGuid(), 30, 10);
        claimedIds.Should().BeEmpty();
    }
}
```

### Inbox Service Tests

```csharp
public class InboxServiceTests : SqlServerTestBase
{
    [Fact]
    public async Task InboxService_Should_PreventDuplicateProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlInbox(new SqlInboxOptions { ConnectionString = ConnectionString });
        var serviceProvider = services.BuildServiceProvider();
        var inbox = serviceProvider.GetRequiredService<IInbox>();

        // Act - First processing attempt
        var firstCheck = await inbox.AlreadyProcessedAsync("msg-123", "TestSource");
        
        // Assert - Should be false (first time)
        firstCheck.Should().BeFalse();
        
        // Act - Mark as processed
        await inbox.MarkProcessedAsync("msg-123");
        
        // Act - Second processing attempt
        var secondCheck = await inbox.AlreadyProcessedAsync("msg-123", "TestSource");
        
        // Assert - Should be true (already processed)
        secondCheck.Should().BeTrue();
    }

    [Fact]
    public async Task InboxService_Should_HandleConcurrentAccess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlInbox(new SqlInboxOptions { ConnectionString = ConnectionString });
        var serviceProvider = services.BuildServiceProvider();
        var inbox = serviceProvider.GetRequiredService<IInbox>();

        // Act - Simulate concurrent processing of same message
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var isProcessed = await inbox.AlreadyProcessedAsync("concurrent-msg", "TestSource");
            if (!isProcessed)
            {
                await Task.Delay(50); // Simulate processing time
                await inbox.MarkProcessedAsync("concurrent-msg");
            }
            return isProcessed;
        });

        var results = await Task.WhenAll(tasks);

        // Assert - Exactly one should have processed (returned false)
        results.Count(r => !r).Should().Be(1);
        results.Count(r => r).Should().Be(9);
    }
}
```

### Lease System Tests

```csharp
public class LeaseSystemTests : SqlServerTestBase
{
    [Fact]
    public async Task LeaseRunner_Should_AcquireAndRenewLease()
    {
        // Arrange
        var leaseApi = new LeaseApi(ConnectionString, "infra");
        var clock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = new NullLogger<LeaseRunner>();

        // Act - Acquire lease
        var runner = await LeaseRunner.AcquireAsync(
            leaseApi, clock, timeProvider,
            "test-lease", "test-owner", TimeSpan.FromSeconds(10),
            renewPercent: 0.5, logger: logger);

        // Assert
        runner.Should().NotBeNull();
        runner!.CancellationToken.IsCancellationRequested.Should().BeFalse();

        // Act - Manual renewal
        var renewed = await runner.TryRenewNowAsync();
        
        // Assert
        renewed.Should().BeTrue();

        // Cleanup
        await runner.DisposeAsync();
    }

    [Fact]
    public async Task LeaseRunner_Should_PreventDuplicateAcquisition()
    {
        // Arrange
        var leaseApi = new LeaseApi(ConnectionString, "infra");
        var clock = new MonotonicClock();
        var timeProvider = TimeProvider.System;
        var logger = new NullLogger<LeaseRunner>();

        // Act - First acquisition
        var runner1 = await LeaseRunner.AcquireAsync(
            leaseApi, clock, timeProvider,
            "exclusive-lease", "owner-1", TimeSpan.FromMinutes(1));

        // Act - Second acquisition attempt
        var runner2 = await LeaseRunner.AcquireAsync(
            leaseApi, clock, timeProvider,
            "exclusive-lease", "owner-2", TimeSpan.FromMinutes(1));

        // Assert
        runner1.Should().NotBeNull();
        runner2.Should().BeNull();

        // Cleanup
        await runner1!.DisposeAsync();
    }
}
```

### Test Utilities and Helpers

```csharp
public static class TestHelpers
{
    public static async Task WaitForConditionAsync(
        Func<Task<bool>> condition, 
        TimeSpan timeout = default,
        TimeSpan pollInterval = default)
    {
        timeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
        pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(100) : pollInterval;
        
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
                return;
                
            await Task.Delay(pollInterval);
        }
        
        throw new TimeoutException($"Condition was not met within {timeout}");
    }

    public static IServiceProvider CreateTestServices(string connectionString)
    {
        var services = new ServiceCollection();
        
        services.AddSqlScheduler(new SqlSchedulerOptions
        {
            ConnectionString = connectionString,
            EnableSchemaDeployment = false,
            EnableBackgroundWorkers = false
        });
        
        services.AddLogging(builder => builder.AddXUnit());
        
        return services.BuildServiceProvider();
    }
}
```

### Performance and Load Testing

```csharp
[Collection("Database")]
public class PerformanceTests : SqlServerTestBase
{
    [Fact]
    public async Task OutboxService_Should_HandleHighThroughput()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlOutbox(new SqlOutboxOptions { ConnectionString = ConnectionString });
        var serviceProvider = services.BuildServiceProvider();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        const int messageCount = 1000;
        const int workerCount = 5;

        // Act - Enqueue messages
        var enqueueTasks = Enumerable.Range(0, messageCount).Select(async i =>
        {
            await outbox.EnqueueAsync($"Topic-{i % 10}", $"Payload-{i}", $"correlation-{i}");
        });
        
        var enqueueStopwatch = Stopwatch.StartNew();
        await Task.WhenAll(enqueueTasks);
        enqueueStopwatch.Stop();

        // Act - Process messages with multiple workers
        var workerTasks = Enumerable.Range(0, workerCount).Select(async workerId =>
        {
            var ownerToken = Guid.NewGuid();
            var processed = 0;

            while (processed < messageCount / workerCount + 100) // Process extra to handle distribution
            {
                var claimed = await outbox.ClaimAsync(ownerToken, 30, 50);
                if (claimed.Count == 0)
                {
                    await Task.Delay(10);
                    continue;
                }

                await outbox.AckAsync(ownerToken, claimed);
                processed += claimed.Count;
            }

            return processed;
        });

        var processStopwatch = Stopwatch.StartNew();
        var processedCounts = await Task.WhenAll(workerTasks);
        processStopwatch.Stop();

        // Assert
        var totalProcessed = processedCounts.Sum();
        totalProcessed.Should().BeGreaterOrEqualTo(messageCount);
        
        var enqueueRate = messageCount / enqueueStopwatch.Elapsed.TotalSeconds;
        var processRate = totalProcessed / processStopwatch.Elapsed.TotalSeconds;
        
        // Log performance metrics
        Console.WriteLine($"Enqueue rate: {enqueueRate:F2} msg/sec");
        Console.WriteLine($"Process rate: {processRate:F2} msg/sec");
        
        enqueueRate.Should().BeGreaterThan(100); // Expect > 100 msg/sec enqueue
        processRate.Should().BeGreaterThan(50);  // Expect > 50 msg/sec process
    }
}
```

## Health Monitoring

The platform includes comprehensive health checks for monitoring system health and diagnosing issues.

### Health Check Configuration

```csharp
// Basic health check setup
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck("scheduler")
    .AddSqlServer(connectionString, name: "database");

// Advanced health check configuration
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck("scheduler", 
        tags: new[] { "scheduler", "critical" },
        timeout: TimeSpan.FromSeconds(10))
    .AddSqlServer(connectionString, 
        healthQuery: "SELECT 1", 
        name: "database",
        tags: new[] { "database", "critical" })
    .AddCheck<CustomOutboxHealthCheck>("outbox-processing")
    .AddCheck<TimerAccuracyHealthCheck>("timer-accuracy");

// Configure health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("critical")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions  
{
    Predicate = check => check.Tags.Contains("scheduler")
});
```

### Built-in Health Checks

The `AddSqlSchedulerHealthCheck()` method registers health checks that verify:

1. **Database Connectivity**: Can connect to SQL Server
2. **Schema Validation**: Required tables and procedures exist
3. **Background Services**: Polling services are running
4. **Work Queue Health**: No excessive stuck items
5. **Lease System**: Lease renewal is functioning

### Custom Health Checks

```csharp
public class CustomOutboxHealthCheck : IHealthCheck
{
    private readonly IOutbox _outbox;
    private readonly ILogger<CustomOutboxHealthCheck> _logger;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for excessive pending messages
            var ownerToken = Guid.NewGuid();
            var pendingMessages = await _outbox.ClaimAsync(ownerToken, 1, 1000, cancellationToken);
            
            // Abandon immediately - just checking count
            if (pendingMessages.Count > 0)
            {
                await _outbox.AbandonAsync(ownerToken, pendingMessages, cancellationToken);
            }

            if (pendingMessages.Count > 10000)
            {
                return HealthCheckResult.Degraded(
                    $"High number of pending outbox messages: {pendingMessages.Count}");
            }

            if (pendingMessages.Count > 50000)
            {
                return HealthCheckResult.Unhealthy(
                    $"Excessive pending outbox messages: {pendingMessages.Count}");
            }

            return HealthCheckResult.Healthy($"Outbox processing normal. Pending: {pendingMessages.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox health check failed");
            return HealthCheckResult.Unhealthy("Outbox health check failed", ex);
        }
    }
}

public class TimerAccuracyHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var connectionString = scope.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<SqlSchedulerOptions>>().Value.ConnectionString;

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Check for timers that are significantly overdue
        var query = @"
            SELECT COUNT(*) 
            FROM infra.Timers 
            WHERE StatusCode = 0 
              AND DueTime < DATEADD(minute, -5, GETUTCDATE())";

        using var command = new SqlCommand(query, connection);
        var overdueCount = (int)await command.ExecuteScalarAsync(cancellationToken);

        if (overdueCount > 100)
        {
            return HealthCheckResult.Unhealthy($"Too many overdue timers: {overdueCount}");
        }

        if (overdueCount > 10)
        {
            return HealthCheckResult.Degraded($"Some overdue timers detected: {overdueCount}");
        }

        return HealthCheckResult.Healthy($"Timer accuracy good. Overdue: {overdueCount}");
    }
}
```

### Monitoring Dashboards

Health check data can be consumed by monitoring systems:

```csharp
// For Prometheus/Grafana
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck()
    .ForwardToPrometheus();

// For Application Insights
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.Configure<TelemetryConfiguration>(config =>
{
    config.TelemetryInitializers.Add(new HealthCheckTelemetryInitializer());
});

// Custom metrics for detailed monitoring
public class SchedulerMetricsCollector : BackgroundService
{
    private readonly IMetrics _metrics;
    private readonly IServiceProvider _services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CollectMetricsAsync(stoppingToken);
        }
    }

    private async Task CollectMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var connectionString = scope.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<SqlSchedulerOptions>>().Value.ConnectionString;

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Collect outbox metrics
        var outboxMetrics = await GetOutboxMetricsAsync(connection, cancellationToken);
        _metrics.CreateGauge<int>("outbox_pending_messages").Record(outboxMetrics.Pending);
        _metrics.CreateGauge<int>("outbox_processing_messages").Record(outboxMetrics.Processing);
        _metrics.CreateGauge<int>("outbox_failed_messages").Record(outboxMetrics.Failed);

        // Collect timer metrics
        var timerMetrics = await GetTimerMetricsAsync(connection, cancellationToken);
        _metrics.CreateGauge<int>("timers_pending").Record(timerMetrics.Pending);
        _metrics.CreateGauge<int>("timers_overdue").Record(timerMetrics.Overdue);

        // Collect lease metrics
        var leaseMetrics = await GetLeaseMetricsAsync(connection, cancellationToken);
        _metrics.CreateGauge<int>("active_leases").Record(leaseMetrics.ActiveCount);
    }
}
```

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE) files for licensing and attribution information.

---

## Summary

The Bravellian Platform provides a comprehensive, production-ready solution for distributed scheduling and message processing in .NET applications. Its **work queue pattern** with **atomic claim-ack-abandon semantics** ensures reliable processing even in distributed environments with multiple application instances.

**Key Strengths:**
- **Database-Authoritative Design**: SQL Server provides strong consistency guarantees
- **Integrated Work Queue Pattern**: Eliminates the need for external message brokers for basic scenarios  
- **Monotonic Clock System**: Resilient timing that survives system clock changes and GC pauses
- **Comprehensive Testing**: Full integration test suite with SQL Server containers
- **Production-Ready**: Built-in health checks, monitoring, and operational tooling

**Ideal Use Cases:**
- Applications requiring reliable background processing
- Systems that need distributed coordination without external dependencies
- Scenarios where strong consistency is more important than extreme throughput
- Development teams wanting to avoid the complexity of managing separate message brokers

The platform scales horizontally through database coordination and provides the reliability guarantees needed for mission-critical applications while maintaining simplicity in deployment and operations.
