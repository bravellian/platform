# Inbox Pattern Quick Start

The Inbox pattern ensures at-most-once message processing by tracking which messages have been processed. This prevents duplicate processing when messages are delivered multiple times.

## What is the Inbox Pattern?

The Inbox pattern solves the duplicate message problem. Message systems typically provide at-least-once delivery guarantees, meaning the same message may be delivered multiple times. The Inbox pattern:

1. **Checks** if a message has been processed before
2. **Records** the message as "seen" on first encounter
3. **Skips** processing if the message was already handled
4. **Marks** successful completion to prevent future processing

This guarantees idempotent message processing even with duplicate deliveries.

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

// Add inbox service with SQL Server
builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "dbo",
    TableName = "Inbox",
    EnableSchemaDeployment = true // Automatically creates the Inbox table
});

var app = builder.Build();
app.Run();
```

### Step 2: Use in Message Handlers

```csharp
public class OrderEventConsumer
{
    private readonly IInbox _inbox;
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderEventConsumer> _logger;

    public OrderEventConsumer(
        IInbox inbox,
        IOrderService orderService,
        ILogger<OrderEventConsumer> logger)
    {
        _inbox = inbox;
        _orderService = orderService;
        _logger = logger;
    }

    public async Task HandleOrderCreatedAsync(OrderCreatedMessage message)
    {
        // Step 1: Check if we've already processed this message
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId: message.MessageId,
            source: "OrderService");

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Message {MessageId} already processed, skipping", 
                message.MessageId);
            return;
        }

        try
        {
            // Step 2: Mark as processing (for poison message detection)
            await _inbox.MarkProcessingAsync(message.MessageId);

            // Step 3: Process the message (your business logic)
            await _orderService.ProcessNewOrderAsync(message);

            // Step 4: Mark as successfully processed
            await _inbox.MarkProcessedAsync(message.MessageId);

            _logger.LogInformation(
                "Successfully processed message {MessageId}", 
                message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to process message {MessageId}", 
                message.MessageId);

            // Optional: Mark as dead after repeated failures
            // await _inbox.MarkDeadAsync(message.MessageId);
            throw;
        }
    }
}
```

## Content Hash Verification

For additional safety, you can include a content hash to detect message tampering or corruption:

```csharp
using System.Security.Cryptography;
using System.Text;

public async Task HandleOrderCreatedAsync(OrderCreatedMessage message)
{
    // Compute hash of message content
    var contentJson = JsonSerializer.Serialize(message);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(contentJson));

    // Check if already processed with content verification
    var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
        messageId: message.MessageId,
        source: "OrderService",
        hash: hash);

    if (alreadyProcessed)
    {
        _logger.LogInformation("Message {MessageId} already processed", message.MessageId);
        return;
    }

    // ... process message
}
```

If a message with the same ID but different content arrives, it will be treated as a new message.

## Integration with Message Brokers

### Azure Service Bus Example

```csharp
public class ServiceBusMessageProcessor
{
    private readonly IInbox _inbox;
    private readonly IMessageHandler _handler;
    private readonly ILogger<ServiceBusMessageProcessor> _logger;

    public ServiceBusMessageProcessor(
        IInbox inbox,
        IMessageHandler handler,
        ILogger<ServiceBusMessageProcessor> logger)
    {
        _inbox = inbox;
        _handler = handler;
        _logger = logger;
    }

    public async Task ProcessMessageAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract message details
            var messageId = message.MessageId;
            var source = "ServiceBus";
            var content = message.Body.ToString();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

            // Check for duplicate
            var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
                messageId, source, hash, cancellationToken);

            if (alreadyProcessed)
            {
                // Message already processed - just complete it
                await message.CompleteAsync(cancellationToken);
                return;
            }

            // Mark as processing
            await _inbox.MarkProcessingAsync(messageId, cancellationToken);

            // Process the message
            await _handler.HandleAsync(content, cancellationToken);

            // Mark as processed
            await _inbox.MarkProcessedAsync(messageId, cancellationToken);

            // Complete the message in Service Bus
            await message.CompleteAsync(cancellationToken);

            _logger.LogInformation("Processed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);

            // Mark as dead after multiple failures
            await _inbox.MarkDeadAsync(message.MessageId, cancellationToken);

            // Dead letter the message for manual review
            await message.DeadLetterAsync(
                "ProcessingError",
                ex.Message,
                cancellationToken);
        }
    }
}
```

### RabbitMQ Example

```csharp
public class RabbitMQConsumer : BackgroundService
{
    private readonly IInbox _inbox;
    private readonly IMessageHandler _handler;
    private readonly IConnection _connection;
    private IModel _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateModel();
        _channel.BasicQos(0, 10, false); // Prefetch 10 messages

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (sender, ea) =>
        {
            try
            {
                var messageId = ea.BasicProperties.MessageId;
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());

                // Check if already processed
                var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
                    messageId, "RabbitMQ", cancellationToken: stoppingToken);

                if (alreadyProcessed)
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // Process message
                await _inbox.MarkProcessingAsync(messageId, stoppingToken);
                await _handler.HandleAsync(body, stoppingToken);
                await _inbox.MarkProcessedAsync(messageId, stoppingToken);

                // Acknowledge message
                _channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message");
                
                // Reject and requeue (or not, depending on error)
                _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
            }
        };

        _channel.BasicConsume(
            queue: "orders",
            autoAck: false,
            consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

## Retry Logic with Inbox

Combine inbox with retry logic for resilient processing:

```csharp
public class ResilientMessageHandler
{
    private readonly IInbox _inbox;
    private readonly IMessageProcessor _processor;
    private readonly int _maxRetries = 3;

    public async Task HandleWithRetriesAsync(InboundMessage message)
    {
        // Check if already processed
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            message.Id, message.Source);
            
        if (alreadyProcessed)
        {
            return;
        }

        var attempts = 0;
        Exception? lastException = null;

        while (attempts < _maxRetries)
        {
            try
            {
                await _inbox.MarkProcessingAsync(message.Id);
                
                // Your processing logic here
                await _processor.ProcessAsync(message);
                
                await _inbox.MarkProcessedAsync(message.Id);
                return; // Success!
            }
            catch (TransientException ex)
            {
                // Transient error - retry after delay
                lastException = ex;
                attempts++;
                
                if (attempts < _maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempts));
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                // Non-transient error - don't retry
                _logger.LogError(ex, "Permanent error processing message {MessageId}", message.Id);
                await _inbox.MarkDeadAsync(message.Id);
                throw;
            }
        }
        
        // Max retries exceeded
        await _inbox.MarkDeadAsync(message.Id);
        throw new MaxRetriesExceededException(
            $"Failed after {_maxRetries} attempts", lastException);
    }
}
```

## Inbox with Background Processing

The platform supports enqueueing messages for background processing using inbox handlers:

### Step 1: Create an Inbox Handler

```csharp
public class PaymentReceivedHandler : IInboxHandler
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentReceivedHandler> _logger;

    public PaymentReceivedHandler(
        IPaymentService paymentService,
        ILogger<PaymentReceivedHandler> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    // The topic this handler processes
    public string Topic => "payment.received";

    public async Task HandleAsync(
        InboxMessage message,
        CancellationToken cancellationToken)
    {
        var payment = JsonSerializer.Deserialize<PaymentReceivedEvent>(message.Payload);

        await _paymentService.RecordPaymentAsync(
            payment.OrderId,
            payment.Amount,
            payment.PaymentMethod,
            cancellationToken);

        _logger.LogInformation(
            "Processed payment {PaymentId} for order {OrderId}",
            payment.PaymentId,
            payment.OrderId);
    }
}
```

### Step 2: Register the Handler

```csharp
builder.Services.AddTransient<IInboxHandler, PaymentReceivedHandler>();
```

### Step 3: Enqueue Messages

```csharp
public async Task ReceivePaymentWebhookAsync(PaymentWebhook webhook)
{
    // Enqueue to inbox for background processing
    await _inbox.EnqueueAsync(
        topic: "payment.received",
        source: "StripeWebhook",
        messageId: webhook.Id,
        payload: JsonSerializer.Serialize(webhook),
        hash: ComputeHash(webhook));
}
```

## Database Schema

When `EnableSchemaDeployment = true`, this table is created automatically:

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

## How It Works

### MERGE/Upsert Semantics

The `AlreadyProcessedAsync` method uses SQL MERGE for atomic deduplication:

```sql
MERGE dbo.Inbox AS target
USING (VALUES (@MessageId, @Source, @Hash, @Now)) AS source
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

This ensures:
- **First time**: Message inserted as 'Seen', returns `false`
- **Subsequent times**: Message updated, returns `true` if already processed
- **Concurrent access**: Only one thread "wins" the first processing attempt

## Configuration Options

```csharp
builder.Services.AddSqlInbox(new SqlInboxOptions
{
    // Required: Database connection
    ConnectionString = "Server=localhost;Database=MyApp;...",
    
    // Optional: Schema and table names (defaults to "dbo" and "Inbox")
    SchemaName = "dbo",
    TableName = "Inbox",
    
    // Optional: Automatically create database objects (default: false)
    EnableSchemaDeployment = true
});
```

## Monitoring and Cleanup

Implement regular maintenance to clean up old processed messages:

```csharp
public class InboxMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _services.CreateScope();
                var connectionString = scope.ServiceProvider
                    .GetRequiredService<IOptions<SqlInboxOptions>>()
                    .Value.ConnectionString;
                
                // Clean up old processed messages (older than 30 days)
                await CleanupOldMessagesAsync(connectionString, TimeSpan.FromDays(30));
                
                // Alert on stuck processing messages
                await AlertOnStuckMessagesAsync(connectionString, TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in inbox maintenance");
            }
        }
    }

    private async Task CleanupOldMessagesAsync(
        string connectionString, 
        TimeSpan retentionPeriod)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand(@"
            DELETE FROM dbo.Inbox 
            WHERE Status = 'Done' 
              AND ProcessedUtc < @CutoffDate",
            connection);
        
        command.Parameters.AddWithValue("@CutoffDate", 
            DateTime.UtcNow.Subtract(retentionPeriod));
        
        var deleted = await command.ExecuteNonQueryAsync();
        _logger.LogInformation("Cleaned up {Count} old inbox messages", deleted);
    }
}
```

## Testing

For testing, you can mock the `IInbox` interface:

```csharp
[Fact]
public async Task HandleMessage_SkipsWhenAlreadyProcessed()
{
    // Arrange
    var mockInbox = new Mock<IInbox>();
    mockInbox.Setup(x => x.AlreadyProcessedAsync(
        It.IsAny<string>(), 
        It.IsAny<string>(), 
        It.IsAny<byte[]?>(), 
        It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    var handler = new OrderEventConsumer(
        mockInbox.Object,
        Mock.Of<IOrderService>(),
        Mock.Of<ILogger<OrderEventConsumer>>());

    // Act
    await handler.HandleOrderCreatedAsync(new OrderCreatedMessage
    {
        MessageId = "msg-123"
    });

    // Assert
    mockInbox.Verify(x => x.MarkProcessingAsync(
        It.IsAny<string>(), 
        It.IsAny<CancellationToken>()), Times.Never);
}
```

## Next Steps

- [Inbox API Reference](inbox-api-reference.md) - Complete API documentation
- [Inbox Examples](inbox-examples.md) - More real-world examples
- [Multi-Tenant Inbox](InboxRouter.md) - Using inbox with multiple databases
- [Outbox Pattern](outbox-quickstart.md) - Reliable message publishing

## Common Patterns

### Pattern 1: Event Sourcing

```csharp
public class EventStoreConsumer
{
    private readonly IInbox _inbox;
    private readonly IEventStore _eventStore;

    public async Task HandleEventAsync(StoredEvent evt)
    {
        // Use event ID and version as message ID
        var messageId = $"{evt.StreamId}-{evt.Version}";
        
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId, "EventStore");
            
        if (alreadyProcessed)
        {
            return;
        }

        await _inbox.MarkProcessingAsync(messageId);
        
        // Project event into read model
        await ProjectEventAsync(evt);
        
        await _inbox.MarkProcessedAsync(messageId);
    }
}
```

### Pattern 2: Webhook Receiver

```csharp
[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IInbox _inbox;

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook([FromBody] StripeEvent evt)
    {
        // Use Stripe's event ID
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            evt.Id, "Stripe");
            
        if (alreadyProcessed)
        {
            return Ok(); // Idempotent response
        }

        // Enqueue for background processing
        await _inbox.EnqueueAsync(
            topic: "stripe.event",
            source: "Stripe",
            messageId: evt.Id,
            payload: JsonSerializer.Serialize(evt));

        return Ok();
    }
}
```

## Troubleshooting

### Duplicate Processing Still Occurring

Verify you're using the correct message ID:
- Message IDs must be **stable** across retries
- Use broker-provided IDs (e.g., Service Bus `MessageId`)
- Don't generate new IDs on retry

### Inbox Table Growing Too Large

Implement cleanup maintenance:
- Delete old processed messages regularly
- Consider partitioning by date for very high volumes
- Archive instead of delete if needed for auditing

### Deadlocked Messages

Check for stuck "Processing" messages:

```sql
SELECT MessageId, Source, LastSeenUtc, Attempts
FROM dbo.Inbox
WHERE Status = 'Processing'
  AND LastSeenUtc < DATEADD(hour, -1, GETUTCDATE());
```

## Summary

The Inbox pattern provides:

✅ **At-most-once processing** - Messages processed exactly once  
✅ **Duplicate detection** - Same message ID is skipped  
✅ **Content verification** - Optional hash prevents corruption  
✅ **Poison message handling** - Track and isolate problem messages  
✅ **Concurrent safety** - SQL MERGE ensures atomicity  

Combined with the [Outbox pattern](outbox-quickstart.md), you get end-to-end exactly-once semantics across distributed systems.
