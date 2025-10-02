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

### Database Schema

The platform automatically creates the required database schema when `EnableSchemaDeployment = true`. Alternatively, you can run the SQL scripts manually:

```bash
# Core tables and stored procedures
src/Bravellian.Platform.Database/
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
CREATE INDEX IX_{Table}_WorkQueue ON dbo.{Table}(Status, {TimeColumn}) 
    INCLUDE(Id, OwnerToken);
```

#### Stored Procedures Pattern

Each table has five generated stored procedures following this pattern:

```sql
-- Example for Outbox table
dbo.Outbox_Claim        -- Atomically claim ready items
dbo.Outbox_Ack          -- Mark items as successfully completed
dbo.Outbox_Abandon      -- Return items to ready state for retry
dbo.Outbox_Fail         -- Mark items as failed
dbo.Outbox_ReapExpired  -- Recover expired leases
```

**Claim Procedure Logic** (simplified):
```sql
CREATE OR ALTER PROCEDURE dbo.Outbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM dbo.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0 /* Ready */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET 
        Status = 1 /* InProgress */, 
        OwnerToken = @OwnerToken, 
        LockedUntil = @until
    OUTPUT inserted.Id
    FROM dbo.Outbox o
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
- **`dbo.Lease` table**: Stores lease information with DB-authoritative timestamps
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
CREATE TABLE dbo.Outbox (
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
CREATE INDEX IX_Outbox_WorkQueue ON dbo.Outbox(Status, CreatedAt) 
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
CREATE TABLE dbo.Inbox (
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,                    -- Optional content verification
    FirstSeenUtc DATETIME2(3) NOT NULL,
    LastSeenUtc DATETIME2(3) NOT NULL,
    ProcessedUtc DATETIME2(3) NULL,
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'  -- Seen, Processing, Done, Dead
);

CREATE INDEX IX_Inbox_Processing ON dbo.Inbox(Status, LastSeenUtc)
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
MERGE dbo.Inbox AS target
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
CREATE TABLE dbo.Timers (
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
CREATE INDEX IX_Timers_WorkQueue ON dbo.Timers(StatusCode, DueTime) 
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
CREATE OR ALTER PROCEDURE dbo.Timers_Claim
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
        FROM dbo.Timers WITH (READPAST, UPDLOCK, ROWLOCK)
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
    FROM dbo.Timers t
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

Schedule recurring jobs using cron expressions for repeated execution.

### Basic Usage

```csharp
public class MaintenanceService
{
    private readonly ISchedulerClient _scheduler;

    public MaintenanceService(ISchedulerClient scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task SetupRecurringJobsAsync()
    {
        // Run daily cleanup at 2 AM
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "DailyCleanup",
            topic: "RunCleanup",
            cronSchedule: "0 0 2 * * *", // Every day at 2:00 AM
            payload: JsonSerializer.Serialize(new CleanupConfig 
            { 
                RetentionDays = 90,
                IncludeArchives = true 
            }));

        // Generate reports every Monday at 9 AM
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "WeeklyReport",
            topic: "GenerateReport",
            cronSchedule: "0 0 9 * * 1", // Every Monday at 9:00 AM
            payload: JsonSerializer.Serialize(new ReportConfig 
            { 
                ReportType = "Weekly",
                Recipients = new[] { "admin@company.com" }
            }));
    }

    public async Task DeleteJobAsync()
    {
        // Delete a job and all its pending runs
        await _scheduler.DeleteJobAsync("DailyCleanup");
    }

    public async Task TriggerJobNowAsync()
    {
        // Trigger a job immediately, outside its normal schedule
        await _scheduler.TriggerJobAsync("DailyCleanup");
    }
}
```

### Cron Expression Examples

```csharp
// Every 5 minutes
"0 */5 * * * *"

// Every hour at minute 0
"0 0 * * * *"

// Every day at 2:30 AM
"0 30 2 * * *"

// Every Monday at 9 AM
"0 0 9 * * 1"

// Every first day of the month at midnight
"0 0 0 1 * *"

// Every weekday at 6 PM
"0 0 18 * * 1-5"
```

### Processing Job Events

Similar to timers, job executions create outbox messages:

```csharp
public class CleanupHandler
{
    public async Task HandleCleanupAsync(string payload)
    {
        var config = JsonSerializer.Deserialize<CleanupConfig>(payload);
        
        // Perform cleanup operations
        await DeleteOldRecordsAsync(config.RetentionDays);
        
        if (config.IncludeArchives)
        {
            await CleanupArchivesAsync();
        }
    }
}
```

### Key Features

- **Cron-based**: Flexible scheduling using standard cron expressions
- **Persistent**: Job definitions survive application restarts
- **Reliable**: Uses distributed locking for consistent execution
- **Manageable**: Create, update, delete, and enable/disable jobs
- **Triggerable**: Run jobs immediately outside their normal schedule
- **Auditable**: Track execution history and results

### Job Management

```csharp
public class JobManagementService
{
    private readonly ISchedulerClient _scheduler;

    public JobManagementService(ISchedulerClient scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task CreateJobAsync()
    {
        // Create or update a job (idempotent operation)
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "DataBackup",
            topic: "BackupDatabase",
            cronSchedule: "0 0 3 * * *", // Daily at 3 AM
            payload: JsonSerializer.Serialize(new BackupConfig 
            { 
                DatabaseName = "Production",
                RetentionDays = 30 
            }));
    }

    public async Task UpdateJobScheduleAsync()
    {
        // Update existing job (changes schedule and payload)
        await _scheduler.CreateOrUpdateJobAsync(
            jobName: "DataBackup",
            topic: "BackupDatabase", 
            cronSchedule: "0 0 2 * * *", // Change to 2 AM
            payload: JsonSerializer.Serialize(new BackupConfig 
            { 
                DatabaseName = "Production",
                RetentionDays = 45 // Longer retention
            }));
    }

    public async Task DeleteJobAsync()
    {
        // Delete job and all pending runs
        await _scheduler.DeleteJobAsync("DataBackup");
    }

    public async Task TriggerJobNowAsync()
    {
        // Run job immediately (doesn't affect normal schedule)
        await _scheduler.TriggerJobAsync("DataBackup");
    }
}

## Configuration

### Full Configuration Example

```csharp
// appsettings.json
{
  "SqlScheduler": {
    "ConnectionString": "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    "MaxPollingInterval": "00:00:30",
    "EnableBackgroundWorkers": true
  }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Option 1: Use configuration binding
builder.Services.AddSqlScheduler(builder.Configuration);

// Option 2: Configure with options
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default"),
    MaxPollingInterval = TimeSpan.FromSeconds(30),
    EnableBackgroundWorkers = true
});

// Option 3: Individual components
builder.Services.AddSqlDistributedLock(new SqlDistributedLockOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")
});

builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = builder.Configuration.GetConnectionString("Default")
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck();

var app = builder.Build();

// Configure health check endpoint
app.MapHealthChecks("/health");
```

### Configuration Options

#### SqlSchedulerOptions

- **ConnectionString**: SQL Server connection string (required)
- **MaxPollingInterval**: Maximum time between polling cycles (default: 30 seconds)
- **EnableBackgroundWorkers**: Whether to start background processing services (default: true)

#### SqlDistributedLockOptions

- **ConnectionString**: SQL Server connection string (required)

#### SqlOutboxOptions

- **ConnectionString**: SQL Server connection string (required)

## Database Schema

The platform requires four tables in your SQL Server database:

### Outbox Table
```sql
CREATE TABLE dbo.Outbox (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CorrelationId UNIQUEIDENTIFIER NULL
);
```

### Timers Table
```sql
CREATE TABLE dbo.Timers (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    DueTime DATETIMEOFFSET NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CorrelationId NVARCHAR(255) NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ProcessedAt DATETIMEOFFSET NULL,
    LastError NVARCHAR(MAX) NULL
);
```

### Jobs Table
```sql
CREATE TABLE dbo.Jobs (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobName NVARCHAR(100) NOT NULL,
    CronSchedule NVARCHAR(100) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,
    NextDueTime DATETIMEOFFSET NULL,
    LastRunTime DATETIMEOFFSET NULL,
    LastRunStatus NVARCHAR(20) NULL
);
```

### JobRuns Table
```sql
CREATE TABLE dbo.JobRuns (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Jobs(Id),
    ScheduledTime DATETIMEOFFSET NOT NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    StartTime DATETIMEOFFSET NULL,
    EndTime DATETIMEOFFSET NULL,
    Output NVARCHAR(MAX) NULL,
    LastError NVARCHAR(MAX) NULL
);
```

## Key Assumptions and Considerations

### Database Requirements

- **SQL Server**: The platform requires SQL Server (2012 or later)
- **Connection Pool**: Ensure your connection pool can handle the background workers
- **Permissions**: The application needs permissions to create locks (`sp_getapplock`) and perform standard CRUD operations

### Scalability Considerations

- **Multiple Instances**: All components are designed to work with multiple application instances
- **Distributed Locking**: Prevents duplicate processing across instances
- **Polling Intervals**: Adjust `MaxPollingInterval` based on your throughput requirements
- **Database Load**: Background workers will continuously poll the database

### Reliability Features

- **Transactional Consistency**: Outbox messages are part of your business transactions
- **Automatic Retries**: Failed messages and timers are retried with exponential backoff
- **Poison Message Handling**: Messages that fail repeatedly can be manually investigated
- **Health Checks**: Built-in health checks for monitoring

### Performance Tips

- **Indexes**: The provided SQL scripts include optimized indexes for performance
- **Connection Pooling**: Use connection pooling in your connection string
- **Batch Processing**: The outbox processor handles multiple messages in batches
- **Monitoring**: Use the health checks and metrics for monitoring

### Message Handling

Your application needs to implement message handlers for the topics you use:

```csharp
public interface IMessageHandler
{
    Task HandleAsync(string topic, string payload);
}

// Implement handlers for your specific topics
public class OrderEventHandler : IMessageHandler
{
    public async Task HandleAsync(string topic, string payload)
    {
        switch (topic)
        {
            case "OrderCreated":
                await HandleOrderCreated(payload);
                break;
            case "SendReminder":
                await HandleReminder(payload);
                break;
            // Add other topic handlers
        }
    }
}
```

### Error Handling Best Practices

- **Idempotent Handlers**: Ensure your message handlers can be safely retried
- **Correlation IDs**: Use correlation IDs for tracing messages across systems
- **Error Logging**: Log errors with sufficient context for debugging
- **Dead Letter Handling**: Plan for messages that fail permanently

## Testing

The platform includes test utilities for integration testing with SQL Server.

### Test Setup

```csharp
public class MyServiceTests : SqlServerTestBase
{
    public MyServiceTests(ITestOutputHelper testOutputHelper) 
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task Should_Process_Outbox_Messages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlOutbox(new SqlOutboxOptions 
        { 
            ConnectionString = ConnectionString 
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();

        // Act & Assert
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        
        await outbox.EnqueueAsync("TestTopic", "TestPayload", transaction);
        await transaction.CommitAsync();
        
        // Verify message was stored
        // Add your assertions here
    }
}
```

### Test Containers

The test base uses Testcontainers to spin up a real SQL Server instance:

```csharp
// Automatically uses SQL Server 2022 container
// Schema is created automatically from SQL scripts
// Clean database for each test class
```

## Health Monitoring

The platform includes health checks for monitoring:

```csharp
builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck("scheduler", tags: new[] { "database", "scheduler" });

app.MapHealthChecks("/health");
```

Health checks verify:
- Database connectivity
- Table existence and schema
- Background worker status

## License

See LICENSE and NOTICE for licensing and attribution information.
