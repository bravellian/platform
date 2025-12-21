# Outbox Pattern Quick Start

The Outbox pattern ensures reliable message publishing by storing outbound messages in the same database transaction as your business operations. This guide will get you started quickly.

If you need the big-picture view of how inbox, outbox, fanout, and fan-in fit together, start with the [Platform Primitives Overview](platform-primitives-overview.md).

## What is the Outbox Pattern?

The Outbox pattern solves the dual-write problem: ensuring that database changes and message publishing happen atomically. Instead of directly publishing to a message broker, you:

1. **Write** messages to an outbox table in the same transaction as your business data
2. **Process** messages asynchronously using a background worker
3. **Publish** messages to your actual destination (message broker, API, etc.)

This guarantees that if your transaction commits, your message will eventually be published.

## Installation

Add the Bravellian Platform package:

```bash
dotnet add package Bravellian.Platform
```

## Basic Setup

### Step 1: Configure Services

```csharp
using Bravellian.Platform;

var builder = WebApplication.CreateBuilder(args);

// Add outbox service with SQL Server
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "dbo",
    TableName = "Outbox",
    EnableSchemaDeployment = true // Automatically creates the Outbox table
});

var app = builder.Build();
app.Run();
```

### Step 2: Use in Your Code

```csharp
public class OrderService
{
    private readonly IOutbox _outbox;
    private readonly IDbConnection _connection;

    public OrderService(IOutbox outbox, IDbConnection connection)
    {
        _outbox = outbox;
        _connection = connection;
    }

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        // Open connection and begin transaction
        await _connection.OpenAsync();
        using var transaction = _connection.BeginTransaction();

        try
        {
            // Step 1: Save your business data
            var orderId = await SaveOrderToDatabase(request, transaction);

            // Step 2: Enqueue outbox message in SAME transaction
            await _outbox.EnqueueAsync(
                topic: "order.created",
                payload: JsonSerializer.Serialize(new OrderCreatedEvent
                {
                    OrderId = orderId,
                    CustomerId = request.CustomerId,
                    Amount = request.Amount,
                    CreatedAt = DateTime.UtcNow
                }),
                transaction: transaction,
                correlationId: request.RequestId);

            // Step 3: Commit both together
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<Guid> SaveOrderToDatabase(
        CreateOrderRequest request, 
        IDbTransaction transaction)
    {
        var orderId = Guid.NewGuid();
        
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Orders (Id, CustomerId, Amount, Status, CreatedAt)
            VALUES (@Id, @CustomerId, @Amount, 'Pending', @CreatedAt)";
        
        // Add parameters...
        await command.ExecuteNonQueryAsync();
        
        return orderId;
    }
}
```

### Step 3: Create Message Handlers

```csharp
public class OrderCreatedHandler : IOutboxHandler
{
    private readonly IMessageBroker _messageBroker;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(
        IMessageBroker messageBroker, 
        ILogger<OrderCreatedHandler> logger)
    {
        _messageBroker = messageBroker;
        _logger = logger;
    }

    // The topic this handler processes
    public string Topic => "order.created";

    public async Task HandleAsync(
        OutboxMessage message, 
        CancellationToken cancellationToken)
    {
        // Deserialize the event
        var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload);

        // Publish to your message broker (e.g., RabbitMQ, Azure Service Bus)
        await _messageBroker.PublishAsync(
            exchange: "orders",
            routingKey: "order.created",
            body: message.Payload,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Published order.created event for order {OrderId}", 
            orderEvent.OrderId);
    }
}
```

### Step 4: Register Handlers

```csharp
// In Program.cs
builder.Services.AddTransient<IOutboxHandler, OrderCreatedHandler>();
```

## Standalone Usage (Without Transaction)

If you don't need to coordinate with a database transaction, you can enqueue messages directly:

```csharp
public class NotificationService
{
    private readonly IOutbox _outbox;

    public NotificationService(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public async Task SendWelcomeEmailAsync(string userId, string email)
    {
        // Creates its own connection and transaction
        await _outbox.EnqueueAsync(
            topic: "email.welcome",
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

## How It Works

### Background Processing

The platform automatically starts a background service that:

1. **Claims** messages from the outbox table (using atomic database operations)
2. **Processes** each message by calling the appropriate `IOutboxHandler`
3. **Acknowledges** successful messages or **abandons** failed ones for retry
4. **Reaps** expired leases to recover from worker crashes

### Work Queue Semantics

Messages follow the **claim-ack-abandon** pattern:

- **Claim**: Worker atomically claims messages with a lease
- **Ack**: Mark as processed after successful handling
- **Abandon**: Return to ready state for retry after failure
- **Reap**: Automatically recover messages when lease expires

### Retry and Error Handling

Failed messages are automatically retried with exponential backoff:

```
Attempt 1: Immediate
Attempt 2: 1 second delay
Attempt 3: 2 seconds delay
Attempt 4: 4 seconds delay
Attempt 5: 8 seconds delay
...
Maximum: 60 seconds delay
```

## Database Schema

When `EnableSchemaDeployment = true`, this table is created automatically:

```sql
CREATE TABLE dbo.Outbox (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Work queue columns
    Status TINYINT NOT NULL DEFAULT(0),        -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL,
    
    -- Error handling
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Tracking
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CorrelationId NVARCHAR(255) NULL,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL
);
```

## Configuration Options

```csharp
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    // Required: Database connection
    ConnectionString = "Server=localhost;Database=MyApp;...",
    
    // Optional: Schema and table names (defaults to "dbo" and "Outbox")
    SchemaName = "dbo",
    TableName = "Outbox",
    
    // Optional: Automatically create database objects (default: false)
    EnableSchemaDeployment = true
});
```

## Testing

For testing, you can mock the `IOutbox` interface:

```csharp
[Fact]
public async Task CreateOrder_EnqueuesOutboxMessage()
{
    // Arrange
    var mockOutbox = new Mock<IOutbox>();
    var service = new OrderService(mockOutbox.Object, mockConnection.Object);

    // Act
    await service.CreateOrderAsync(new CreateOrderRequest
    {
        CustomerId = Guid.NewGuid(),
        Amount = 100.00m
    });

    // Assert
    mockOutbox.Verify(x => x.EnqueueAsync(
        "order.created",
        It.IsAny<string>(),
        It.IsAny<IDbTransaction>(),
        It.IsAny<string>()), Times.Once);
}
```

## Next Steps

- [Outbox API Reference](outbox-api-reference.md) - Complete API documentation
- [Outbox Examples](outbox-examples.md) - More real-world examples
- [Multi-Tenant Outbox](OutboxRouter.md) - Using outbox with multiple databases
- [Work Queue Pattern](work-queue-pattern.md) - Understanding the underlying pattern

## Common Patterns

### Pattern 1: Domain Events

```csharp
public class Order
{
    private readonly List<IDomainEvent> _events = new();
    
    public void Complete()
    {
        Status = OrderStatus.Completed;
        _events.Add(new OrderCompletedEvent(Id, CompletedAt));
    }
    
    public IReadOnlyList<IDomainEvent> GetUncommittedEvents() => _events;
}

// In your repository
public async Task SaveAsync(Order order, IDbTransaction transaction)
{
    // Save order
    await SaveOrderToDatabase(order, transaction);
    
    // Enqueue domain events to outbox
    foreach (var evt in order.GetUncommittedEvents())
    {
        await _outbox.EnqueueAsync(
            topic: evt.GetType().Name,
            payload: JsonSerializer.Serialize(evt),
            transaction: transaction,
            correlationId: order.Id.ToString());
    }
}
```

### Pattern 2: Saga Orchestration

```csharp
public class OrderSagaHandler : IOutboxHandler
{
    public string Topic => "order.created";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
        
        // Start saga by enqueueing next step
        await _outbox.EnqueueAsync(
            "payment.process",
            JsonSerializer.Serialize(new ProcessPaymentCommand
            {
                OrderId = order.OrderId,
                Amount = order.Amount
            }),
            correlationId: message.CorrelationId);
    }
}
```

### Pattern 3: Webhook Notifications

```csharp
public class WebhookHandler : IOutboxHandler
{
    private readonly HttpClient _httpClient;
    
    public string Topic => "webhook.notify";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var webhook = JsonSerializer.Deserialize<WebhookEvent>(message.Payload);
        
        var response = await _httpClient.PostAsync(
            webhook.Url,
            new StringContent(webhook.Payload, Encoding.UTF8, "application/json"),
            ct);
        
        response.EnsureSuccessStatusCode();
    }
}
```

## Troubleshooting

### Messages Not Processing

Check that the background worker is enabled:

```csharp
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "...",
    EnableSchemaDeployment = true
    // Note: Background worker is enabled by default
});
```

### Slow Processing

Increase batch size or add more worker instances:

```csharp
// The background service processes in batches of 50 by default
// Multiple application instances will automatically share the work
```

### Database Connectivity

Verify your connection string and permissions:

```sql
-- The application needs these permissions:
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Outbox TO [AppUser];
GRANT EXECUTE ON dbo.Outbox_Claim TO [AppUser];
GRANT EXECUTE ON dbo.Outbox_Ack TO [AppUser];
GRANT EXECUTE ON dbo.Outbox_Abandon TO [AppUser];
```

## Summary

The Outbox pattern provides:

✅ **Atomic operations** - Messages and data change together  
✅ **Reliable delivery** - Messages will eventually be processed  
✅ **Automatic retry** - Failed messages retry with backoff  
✅ **At-least-once** - Messages may be delivered more than once  
✅ **Horizontal scaling** - Multiple workers can process in parallel  

For at-most-once processing on the receiving side, combine with the [Inbox pattern](inbox-quickstart.md).

### Next steps
- Browse the [Outbox Examples](outbox-examples.md) for end-to-end handlers and poison-message handling.
- Review the [Work Queue Implementation](work-queue-implementation.md) to understand leases and stored procedures that power claims.
- Coordinate cross-component work with [Lease Examples](lease-examples.md).
