# Work Queue Demo

This demo application showcases the generic SQL Server work queue pattern implementation in the Bravellian Platform.

## What it demonstrates

The work queue pattern allows for reliable, scalable processing of background tasks using SQL Server as the coordination mechanism. Key features:

- **Claim-and-Process Pattern**: Workers atomically claim items from queues for processing
- **Lease-based Locking**: Items are locked for a specific time period to handle worker failures
- **Concurrency Safety**: Multiple workers can process the same queue without conflicts
- **Idempotent Operations**: Safe to retry operations without side effects
- **Low Contention**: Uses `READPAST` to avoid blocking on locked items

## Prerequisites

- .NET 9.0 or later
- SQL Server (LocalDB, SQL Express, or full SQL Server)

## Usage

```bash
cd examples/WorkQueueDemo
dotnet run "Server=(localdb)\MSSQLLocalDB;Database=WorkQueueDemo;Trusted_Connection=true;TrustServerCertificate=true"
```

Replace the connection string with your SQL Server instance.

## What the demo does

1. **Schema Setup**: Creates or updates database tables with work queue columns
2. **Sample Data**: Inserts test outbox messages and timers
3. **Workers**: Starts background workers that demonstrate:
   - Claiming items from queues
   - Processing items (simulated work)
   - Acknowledging successful completion
   - Abandoning failed items for retry
4. **Real-time Output**: Shows the claim/ack/abandon operations as they happen

## Sample Output

```
Work Queue Demo - Bravellian Platform
=====================================
Using connection string: Server=(localdb)\MSSQLLocalDB;Database=WorkQueueDemo;Trusted_Connection=true;TrustServerCertificate=true

📝 Setting up database schema...
✅ Database schema ready

📝 Adding sample data...
✅ Sample data added
   - 5 outbox messages
   - 1 due timer
   - 1 future timer

🚀 OutboxWorker started (Owner: f47ac10b-58cc-4372-a567-0e02b2c3d479)
⏰ TimerWorker started (Owner: 550e8400-e29b-41d4-a716-446655440000)
📦 Claimed 5 outbox messages
⏰ Claimed 1 due timers
✅ Processed outbox message c9bf9e57-1685-4c89-bafe-15dabe1f2fe5
✅ Executed timer a1b2c3d4-1234-5678-9abc-123456789012
✅ Acknowledged 5 messages
✅ Acknowledged 1 timers
```

## Work Queue Implementation Details

### Core Interfaces

- `IWorkQueue<T>`: Generic interface for claim/ack/abandon operations
- `IOutboxWorkQueue`: Specialized interface for outbox message processing
- `ITimerWorkQueue`: Specialized interface for timer/scheduled task processing

### SQL Operations

The implementation uses atomic SQL operations with proper locking:

```sql
-- Claim operation uses READPAST to avoid blocking
WITH cte AS (
  SELECT TOP (@BatchSize) Id
  FROM dbo.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
  WHERE Status = 0 /* Ready */
    AND (LockedUntil IS NULL OR LockedUntil <= @now)
  ORDER BY CreatedAt
)
UPDATE o
   SET Status = 1 /* InProgress */, 
       OwnerToken = @OwnerToken, 
       LockedUntil = @until
  OUTPUT inserted.Id
  FROM dbo.Outbox o
  JOIN cte ON cte.Id = o.Id;
```

### Worker Pattern

The demo shows the recommended pattern for work queue consumers:

1. **Claim**: Atomically claim a batch of items
2. **Process**: Do the actual work for each item
3. **Ack/Abandon**: Mark items as completed or return them for retry
4. **Backoff**: Wait with jitter when no items are available

## Integration with Your Application

To use work queues in your application:

```csharp
// Add to DI container
services.AddSqlOutbox(connectionString);
services.AddSqlScheduler(connectionString);

// Inject and use
public class MyWorker : BackgroundService
{
    private readonly IOutboxWorkQueue workQueue;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ownerToken = Guid.NewGuid();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var items = await workQueue.ClaimAsync(ownerToken, 30, 10, stoppingToken);
            
            // Process items...
            
            await workQueue.AckAsync(ownerToken, successfulItems, stoppingToken);
            await workQueue.AbandonAsync(ownerToken, failedItems, stoppingToken);
        }
    }
}
```

## Architecture Benefits

- **Scalability**: Add more workers to increase throughput
- **Reliability**: Items are never lost due to lease-based processing
- **Observability**: Built-in owner tracking for monitoring
- **Flexibility**: Works with any table by adding standard columns
- **Performance**: Minimal locking and efficient SQL operations