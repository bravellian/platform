# Bravellian Platform

A robust .NET platform providing SQL-based distributed locking, outbox pattern implementation, and scheduler services for building resilient, scalable applications.

## Overview

This platform provides five core components:

1. **SQL Distributed Lock** - Database-backed distributed locking mechanism
2. **Outbox Service** - Transactional outbox pattern for reliable message publishing
3. **Inbox Service** - At-most-once processing guard for inbound messages
4. **Timer Scheduler** - One-time scheduled tasks with persistence
5. **Job Scheduler** - Recurring jobs with cron-based scheduling

All components are designed to work together seamlessly and provide strong consistency guarantees through SQL Server integration.

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

// Add SQL Scheduler (includes outbox and distributed lock)
builder.Services.AddSqlScheduler(builder.Configuration);

// Or add components individually
builder.Services.AddSqlDistributedLock(new SqlDistributedLockOptions 
{
    ConnectionString = "Your SQL Server connection string"
});

builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Your SQL Server connection string"
});

builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Your SQL Server connection string"
});

var app = builder.Build();
```

### Database Setup

Run the provided SQL scripts to create the required tables:

```sql
-- Required tables (run these scripts in order)
-- 1. Outbox.sql - For outbox pattern
-- 2. Timers.sql - For one-time scheduled tasks
-- 3. Jobs.sql - For recurring jobs
-- 4. JobRuns.sql - For job execution tracking
```

## Components

## 1. SQL Distributed Lock

Provides a distributed locking mechanism using SQL Server's `sp_getapplock` stored procedure.

### Basic Usage

```csharp
public class MyService
{
    private readonly ISqlDistributedLock _distributedLock;

    public MyService(ISqlDistributedLock distributedLock)
    {
        _distributedLock = distributedLock;
    }

    public async Task DoExclusiveWork()
    {
        // Try to acquire lock immediately (TimeSpan.Zero)
        var lockHandle = await _distributedLock.AcquireAsync(
            "MyResourceLock", 
            TimeSpan.Zero);

        await using (lockHandle)
        {
            if (lockHandle == null)
            {
                // Lock not acquired - another instance is working
                return;
            }

            // Lock acquired - do exclusive work
            await ProcessImportantTask();
        } // Lock is automatically released here
    }

    public async Task DoWorkWithWait()
    {
        // Wait up to 30 seconds for the lock
        var lockHandle = await _distributedLock.AcquireAsync(
            "MyResourceLock", 
            TimeSpan.FromSeconds(30));

        await using (lockHandle)
        {
            if (lockHandle == null)
            {
                throw new TimeoutException("Could not acquire lock within timeout");
            }

            await ProcessImportantTask();
        }
    }
}
```

### Key Features

- **Exclusive locking**: Only one instance can hold a named lock at a time
- **Automatic cleanup**: Locks are released when the connection is closed or transaction rolls back
- **Timeout support**: Configure how long to wait for lock acquisition
- **Cancellation support**: Respects CancellationToken for operation cancellation

### Configuration

```csharp
services.AddSqlDistributedLock(new SqlDistributedLockOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;"
});
```

## 2. Outbox Service

Implements the transactional outbox pattern to ensure reliable message publishing alongside database transactions.

### Basic Usage

```csharp
public class OrderService
{
    private readonly ISqlServerContext _dbContext; // Your DB context
    private readonly IOutbox _outbox;

    public OrderService(ISqlServerContext dbContext, IOutbox outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        try
        {
            // 1. Perform your database operations
            var order = new Order 
            { 
                CustomerId = request.CustomerId,
                Total = request.Total 
            };
            
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            // 2. Enqueue outbox message in the same transaction
            await _outbox.EnqueueAsync(
                topic: "OrderCreated",
                payload: JsonSerializer.Serialize(new OrderCreatedEvent 
                { 
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Total = order.Total,
                    CreatedAt = DateTime.UtcNow
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

### How It Works

1. **Enqueue**: Messages are stored in the outbox table within your business transaction
2. **Process**: Background processor (`OutboxProcessor`) polls for unprocessed messages
3. **Deliver**: Messages are sent to your message broker (Service Bus, RabbitMQ, etc.)
4. **Cleanup**: Successfully delivered messages are marked as processed

### Background Processing

The `OutboxProcessor` runs as a hosted service and automatically processes outbox messages:

```csharp
// Automatically registered when you call AddSqlScheduler() or AddSqlOutbox()
// Polls every 5 seconds for new messages
// Uses distributed locking to ensure only one instance processes messages
```

### Configuration

```csharp
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;"
});
```

### Error Handling

The outbox includes built-in retry logic:
- Failed messages are automatically retried with exponential backoff
- Retry count and error details are tracked
- Messages that fail repeatedly can be inspected and manually resolved

## 3. Inbox Service

Implements the Inbox pattern to ensure at-most-once processing of inbound messages. Prevents duplicate handling when the same message is received multiple times.

### Basic Usage

```csharp
public class OrderEventHandler
{
    private readonly IInbox _inbox;
    private readonly IOrderService _orderService;

    public OrderEventHandler(IInbox inbox, IOrderService orderService)
    {
        _inbox = inbox;
        _orderService = orderService;
    }

    public async Task HandleOrderEventAsync(OrderEvent orderEvent)
    {
        // Check if this message was already processed
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId: orderEvent.MessageId,
            source: "OrderService",
            hash: ComputeHash(orderEvent)); // Optional content verification

        if (alreadyProcessed)
        {
            // Safe to ignore - already processed
            return;
        }

        try
        {
            // Mark as being processed (for poison detection)
            await _inbox.MarkProcessingAsync(orderEvent.MessageId);

            // Process the message
            await _orderService.ProcessOrderAsync(orderEvent);

            // Mark as successfully processed
            await _inbox.MarkProcessedAsync(orderEvent.MessageId);
        }
        catch (Exception ex)
        {
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

### How It Works

1. **First Check**: `AlreadyProcessedAsync` atomically checks if the message was already processed
2. **Safe Processing**: If not processed, marks as "Processing" and handles the message
3. **Completion**: Marks as "Done" when successfully processed
4. **Concurrency Safe**: Uses SQL MERGE for atomic upsert operations
5. **Poison Detection**: Tracks attempts and allows marking messages as "Dead"

### Configuration

```csharp
services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "dbo",      // Default
    TableName = "Inbox"      // Default
});

// Or using connection string shorthand
services.AddSqlInbox("Server=localhost;Database=MyApp;Trusted_Connection=true;");
```

### Database Schema

The Inbox service automatically creates the following table:

```sql
CREATE TABLE dbo.Inbox (
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,
    FirstSeenUtc DATETIME2(3) NOT NULL,
    LastSeenUtc DATETIME2(3) NOT NULL,
    ProcessedUtc DATETIME2(3) NULL,
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'
);
```

## 4. Timer Scheduler (One-Time Tasks)

Schedule one-time tasks to be executed at a specific future time.

### Basic Usage

```csharp
public class NotificationService
{
    private readonly ISchedulerClient _scheduler;

    public NotificationService(ISchedulerClient scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task ScheduleReminderAsync(int userId, DateTime reminderTime)
    {
        var payload = JsonSerializer.Serialize(new ReminderPayload 
        { 
            UserId = userId,
            Message = "Don't forget your appointment!"
        });

        var timerId = await _scheduler.ScheduleTimerAsync(
            topic: "SendReminder",
            payload: payload,
            dueTime: reminderTime);

        // Store timerId if you need to cancel later
        Console.WriteLine($"Scheduled reminder: {timerId}");
    }

    public async Task CancelReminderAsync(string timerId)
    {
        var cancelled = await _scheduler.CancelTimerAsync(timerId);
        if (cancelled)
        {
            Console.WriteLine("Reminder cancelled successfully");
        }
        else
        {
            Console.WriteLine("Reminder not found or already processed");
        }
    }
}
```

### Processing Timer Events

Timer events are dispatched through the outbox pattern:

```csharp
// When a timer fires, it automatically creates an outbox message
// Your message handlers should listen for these messages:

public class ReminderHandler
{
    public async Task HandleReminderAsync(string payload)
    {
        var reminder = JsonSerializer.Deserialize<ReminderPayload>(payload);
        
        // Send email, SMS, push notification, etc.
        await SendNotificationAsync(reminder.UserId, reminder.Message);
    }
}
```

### Key Features

- **Persistent**: Timers survive application restarts
- **Reliable**: Uses distributed locking to prevent duplicate execution
- **Scalable**: Multiple instances can run, but only one processes each timer
- **Cancellable**: Cancel timers before they execute

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
