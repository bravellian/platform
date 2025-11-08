# Inbox API Reference

Complete API reference for the Inbox pattern implementation in Bravellian Platform.

## IInbox Interface

The primary interface for tracking and managing processed messages to ensure idempotency.

### Namespace

```csharp
using Bravellian.Platform;
```

### Methods

#### AlreadyProcessedAsync

Checks if a message has been processed, or records it as seen if new. Uses MERGE/UPSERT semantics for atomic operation.

```csharp
Task<bool> AlreadyProcessedAsync(
    string messageId,
    string source,
    byte[]? hash = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `messageId` (string): Unique identifier of the message (must be stable across retries)
- `source` (string): Source system or component that sent the message
- `hash` (byte[]?, optional): Content hash for additional verification
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task<bool>`
- `true` if message was already processed
- `false` if this is the first time seeing this message

**Example:**
```csharp
var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
    messageId: message.MessageId,
    source: "OrderService",
    hash: ComputeHash(message.Content));

if (alreadyProcessed)
{
    _logger.LogInformation("Message {MessageId} already processed, skipping", message.MessageId);
    return;
}

// Process the message...
```

**Important Notes:**
- This method is **atomic** - uses SQL MERGE to handle concurrent calls safely
- On first call: Inserts message as 'Seen' and returns `false`
- On subsequent calls: Updates LastSeenUtc and returns `true` if already processed
- If `hash` is provided, a different hash for the same messageId will be treated as a new message

#### MarkProcessedAsync

Marks a message as successfully processed.

```csharp
Task MarkProcessedAsync(
    string messageId,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `messageId` (string): Unique identifier of the message
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when message is marked as processed

**Example:**
```csharp
try
{
    await _inbox.MarkProcessingAsync(messageId);
    
    // Process message
    await _handler.ProcessAsync(message);
    
    // Mark as successfully processed
    await _inbox.MarkProcessedAsync(messageId);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process message {MessageId}", messageId);
    throw;
}
```

#### MarkProcessingAsync

Marks a message as being processed. Useful for poison message detection.

```csharp
Task MarkProcessingAsync(
    string messageId,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `messageId` (string): Unique identifier of the message
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when message is marked as processing

**Example:**
```csharp
var alreadyProcessed = await _inbox.AlreadyProcessedAsync(messageId, source);
if (alreadyProcessed) return;

// Indicate we're starting to process (increments Attempts counter)
await _inbox.MarkProcessingAsync(messageId);

try
{
    await ProcessMessageAsync(message);
    await _inbox.MarkProcessedAsync(messageId);
}
catch
{
    // Don't mark as dead here - let it retry
    throw;
}
```

#### MarkDeadAsync

Marks a message as dead/poison after repeated failures.

```csharp
Task MarkDeadAsync(
    string messageId,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `messageId` (string): Unique identifier of the message
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when message is marked as dead

**Example:**
```csharp
const int MaxRetries = 3;

var alreadyProcessed = await _inbox.AlreadyProcessedAsync(messageId, source);
if (alreadyProcessed) return;

await _inbox.MarkProcessingAsync(messageId);

try
{
    await ProcessMessageAsync(message);
    await _inbox.MarkProcessedAsync(messageId);
}
catch (Exception ex)
{
    // Check if we've exceeded retry limit
    var attempts = await GetAttemptCountAsync(messageId);
    
    if (attempts >= MaxRetries)
    {
        _logger.LogError(ex, "Message {MessageId} failed {Attempts} times, marking as dead", 
            messageId, attempts);
        await _inbox.MarkDeadAsync(messageId);
    }
    
    throw;
}
```

#### EnqueueAsync

Enqueues a message for background processing by inbox handlers.

```csharp
Task EnqueueAsync(
    string topic,
    string source,
    string messageId,
    string payload,
    byte[]? hash = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `topic` (string): The topic to route the message to an appropriate handler
- `source` (string): The source system or component that sent the message
- `messageId` (string): Unique identifier of the message
- `payload` (string): The message payload content (typically JSON)
- `hash` (byte[]?, optional): Optional content hash for deduplication
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when message is enqueued

**Example:**
```csharp
// Webhook receiver - enqueue for background processing
[HttpPost("webhooks/payment")]
public async Task<IActionResult> PaymentWebhook([FromBody] PaymentEvent evt)
{
    var hash = ComputeHash(evt);
    
    await _inbox.EnqueueAsync(
        topic: "payment.received",
        source: "StripeWebhook",
        messageId: evt.Id,
        payload: JsonSerializer.Serialize(evt),
        hash: hash);
    
    return Ok();
}
```

---

## IInboxHandler Interface

Interface for implementing message handlers for background inbox processing.

### Definition

```csharp
public interface IInboxHandler
{
    /// <summary>
    /// The topic this handler processes.
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// Handles an inbox message.
    /// </summary>
    Task HandleAsync(
        InboxMessage message,
        CancellationToken cancellationToken);
}
```

### Properties

#### Topic

The message topic this handler processes.

```csharp
string Topic { get; }
```

**Example:**
```csharp
public class PaymentReceivedHandler : IInboxHandler
{
    public string Topic => "payment.received";
    
    // ...
}
```

### Methods

#### HandleAsync

Processes a message from the inbox.

```csharp
Task HandleAsync(
    InboxMessage message,
    CancellationToken cancellationToken)
```

**Parameters:**
- `message` (InboxMessage): The message to process
- `cancellationToken` (CancellationToken): Cancellation token

**Returns:** `Task` - Completes when message is handled

**Example:**
```csharp
public async Task HandleAsync(
    InboxMessage message,
    CancellationToken cancellationToken)
{
    var payment = JsonSerializer.Deserialize<PaymentEvent>(message.Payload);
    
    await _paymentService.RecordPaymentAsync(
        payment.OrderId,
        payment.Amount,
        cancellationToken);
    
    _logger.LogInformation(
        "Processed payment {PaymentId} for order {OrderId}",
        payment.PaymentId,
        payment.OrderId);
}
```

---

## IInboxRouter Interface

Interface for routing inbox operations to specific databases in multi-tenant scenarios.

### Methods

#### GetInbox (String)

Gets the inbox for a specific routing key.

```csharp
IInbox GetInbox(string key)
```

**Parameters:**
- `key` (string): The routing key (e.g., tenant ID)

**Returns:** `IInbox` - The inbox instance for this key

**Throws:**
- `ArgumentException` - If key is null or empty
- `InvalidOperationException` - If no inbox exists for the key

**Example:**
```csharp
public async Task HandleWebhookAsync(string tenantId, WebhookEvent evt)
{
    var inbox = _inboxRouter.GetInbox(tenantId);
    
    var alreadyProcessed = await inbox.AlreadyProcessedAsync(
        evt.Id,
        "Webhook");
    
    if (!alreadyProcessed)
    {
        await ProcessEventAsync(evt);
        await inbox.MarkProcessedAsync(evt.Id);
    }
}
```

#### GetInbox (Guid)

Gets the inbox for a specific GUID routing key.

```csharp
IInbox GetInbox(Guid key)
```

**Parameters:**
- `key` (Guid): The routing key (converted to string internally)

**Returns:** `IInbox` - The inbox instance for this key

**Example:**
```csharp
var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var inbox = _inboxRouter.GetInbox(customerId);
```

---

## InboxMessage Class

Represents a message in the inbox.

### Properties

```csharp
public class InboxMessage
{
    public string MessageId { get; set; }
    public string Source { get; set; }
    public string Topic { get; set; }
    public string Payload { get; set; }
    public byte[]? Hash { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? ProcessedUtc { get; set; }
    public int Attempts { get; set; }
    public string Status { get; set; } // "Seen", "Processing", "Done", "Dead"
}
```

**Property Descriptions:**
- `MessageId` - Unique message identifier (stable across retries)
- `Source` - Source system that sent the message
- `Topic` - Message topic for handler routing
- `Payload` - Message content (typically JSON)
- `Hash` - Optional content hash for verification
- `FirstSeenUtc` - When message was first encountered
- `LastSeenUtc` - Most recent encounter time
- `ProcessedUtc` - When message was successfully processed
- `Attempts` - Number of processing attempts
- `Status` - Current status (Seen, Processing, Done, Dead)

---

## SqlInboxOptions Class

Configuration options for SQL Server inbox.

### Properties

```csharp
public class SqlInboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = "Inbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

**Property Descriptions:**
- `ConnectionString` - SQL Server connection string (required)
- `SchemaName` - Database schema name (default: "dbo")
- `TableName` - Inbox table name (default: "Inbox")
- `EnableSchemaDeployment` - Automatically create table (default: false)

### Example

```csharp
builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "dbo",
    TableName = "Inbox",
    EnableSchemaDeployment = true
});
```

---

## Service Registration Extensions

### AddSqlInbox

Registers the SQL Server inbox service.

```csharp
public static IServiceCollection AddSqlInbox(
    this IServiceCollection services,
    SqlInboxOptions options)
```

**Parameters:**
- `services` - The service collection
- `options` - Configuration options

**Returns:** The service collection for chaining

**Example:**
```csharp
builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;...",
    EnableSchemaDeployment = true
});
```

### AddMultiSqlInbox

Registers multiple SQL Server inbox instances for multi-tenant scenarios.

```csharp
public static IServiceCollection AddMultiSqlInbox(
    this IServiceCollection services,
    IEnumerable<SqlInboxOptions> options)
```

**Parameters:**
- `services` - The service collection
- `options` - Collection of configuration options, one per database

**Returns:** The service collection for chaining

**Example:**
```csharp
var tenantDatabases = new[]
{
    new SqlInboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant1;..."
    },
    new SqlInboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant2;..."
    }
};

builder.Services.AddMultiSqlInbox(tenantDatabases);
```

---

## Database Schema

### Inbox Table

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

### MERGE/Upsert Logic

The `AlreadyProcessedAsync` method uses SQL MERGE for atomic deduplication:

```sql
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

**Guarantees:**
- **First call**: Inserts as 'Seen', returns false
- **Concurrent calls**: Only one wins the insert, others update
- **Subsequent calls**: Returns true if ProcessedUtc is set

---

## Content Hash Helper

Utility for computing content hashes for deduplication.

```csharp
using System.Security.Cryptography;
using System.Text;

public static class InboxHashHelper
{
    public static byte[] ComputeHash(string content)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(content));
    }
    
    public static byte[] ComputeHash(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return ComputeHash(json);
    }
}
```

**Example:**
```csharp
var message = await receiveMessageAsync();
var hash = InboxHashHelper.ComputeHash(message.Body);

var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
    message.MessageId,
    "ServiceBus",
    hash);
```

---

## Error Handling

### Common Exceptions

#### ArgumentException
Thrown when invalid parameters are provided.

```csharp
try
{
    await _inbox.AlreadyProcessedAsync(null, "Source");
}
catch (ArgumentException ex)
{
    // Handle null or empty message ID
}
```

#### SqlException
Thrown when database operations fail.

```csharp
try
{
    await _inbox.AlreadyProcessedAsync(messageId, source);
}
catch (SqlException ex)
{
    // Handle database errors (connectivity, permissions, etc.)
}
```

---

## Best Practices

### 1. Use Stable Message IDs

```csharp
// ✅ GOOD: Use broker-provided message ID (stable across retries)
var messageId = serviceBusMessage.MessageId;

// ❌ BAD: Generating new ID on each attempt
var messageId = Guid.NewGuid().ToString();
```

### 2. Include Content Hash for Safety

```csharp
// ✅ GOOD: Content verification prevents corruption
var hash = SHA256.HashData(Encoding.UTF8.GetBytes(message.Body));
var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
    message.MessageId,
    "ServiceBus",
    hash);

// ⚠️ OK: Without hash (less safe but simpler)
var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
    message.MessageId,
    "ServiceBus");
```

### 3. Handle Poison Messages

```csharp
public async Task HandleMessageAsync(Message message)
{
    var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
        message.MessageId,
        "Queue");
    
    if (alreadyProcessed) return;
    
    await _inbox.MarkProcessingAsync(message.MessageId);
    
    try
    {
        await ProcessAsync(message);
        await _inbox.MarkProcessedAsync(message.MessageId);
    }
    catch (Exception ex)
    {
        var attempts = await GetAttemptsAsync(message.MessageId);
        
        if (attempts >= 5)
        {
            // Too many failures - mark as dead
            await _inbox.MarkDeadAsync(message.MessageId);
            await message.DeadLetterAsync("Too many failures", ex.Message);
        }
        else
        {
            // Let it retry
            throw;
        }
    }
}
```

### 4. Use Source to Namespace Messages

```csharp
// ✅ GOOD: Different sources can have same message ID
await _inbox.AlreadyProcessedAsync("msg-123", "ServiceBus");
await _inbox.AlreadyProcessedAsync("msg-123", "RabbitMQ");  // Different message!

// ❌ BAD: Could accidentally dedupe unrelated messages
await _inbox.AlreadyProcessedAsync("msg-123", "Global");
```

### 5. Clean Up Old Messages

```csharp
public class InboxCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Delete processed messages older than 30 days
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);
            
            var command = new SqlCommand(@"
                DELETE FROM dbo.Inbox 
                WHERE Status = 'Done' 
                  AND ProcessedUtc < DATEADD(day, -30, GETUTCDATE())",
                connection);
            
            var deleted = await command.ExecuteNonQueryAsync(stoppingToken);
            _logger.LogInformation("Cleaned up {Count} old inbox messages", deleted);
        }
    }
}
```

---

## Integration Examples

### Azure Service Bus

```csharp
public async Task ProcessMessageAsync(
    ServiceBusReceivedMessage message,
    CancellationToken cancellationToken)
{
    var hash = SHA256.HashData(message.Body);
    
    var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
        message.MessageId,
        "ServiceBus",
        hash,
        cancellationToken);
    
    if (alreadyProcessed)
    {
        await message.CompleteAsync(cancellationToken);
        return;
    }
    
    await _inbox.MarkProcessingAsync(message.MessageId, cancellationToken);
    
    try
    {
        await _handler.HandleAsync(message.Body.ToString(), cancellationToken);
        await _inbox.MarkProcessedAsync(message.MessageId, cancellationToken);
        await message.CompleteAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
        await _inbox.MarkDeadAsync(message.MessageId, cancellationToken);
        await message.DeadLetterAsync("ProcessingError", ex.Message, cancellationToken);
    }
}
```

### RabbitMQ

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

        // Use AsyncEventingBasicConsumer for proper async support
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceivedAsync;

        _channel.BasicConsume(
            queue: "inbox-messages",
            autoAck: false,
            consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var messageId = ea.BasicProperties.MessageId;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());
        
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(messageId, "RabbitMQ");
        
        if (alreadyProcessed)
        {
            _channel.BasicAck(ea.DeliveryTag, false);
            return;
        }
        
        await _inbox.MarkProcessingAsync(messageId);
        
        try
        {
            await _handler.HandleAsync(body, CancellationToken.None);
            await _inbox.MarkProcessedAsync(messageId);
            _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch
        {
            _channel.BasicNack(ea.DeliveryTag, false, requeue: false);
        }
    }
}
```

---

## See Also

- [Inbox Quick Start](inbox-quickstart.md)
- [Inbox Examples](inbox-examples.md)
- [Inbox Router Guide](InboxRouter.md)
- [Outbox Pattern](outbox-quickstart.md)
