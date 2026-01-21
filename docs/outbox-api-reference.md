# Outbox API Reference

Complete API reference for the Outbox pattern implementation in Bravellian Platform.

## IOutbox Interface

The primary interface for enqueueing and managing outbox messages.

### Namespace

```csharp
using Bravellian.Platform;
```

### Methods

#### EnqueueAsync (Standalone)

Enqueues a message using an internal connection and transaction.

```csharp
Task EnqueueAsync(
    string topic,
    string payload,
    string? correlationId = null)
```

**Parameters:**
- `topic` (string): The message topic for routing to handlers
- `payload` (string): The message content (typically JSON)
- `correlationId` (string?, optional): ID to correlate related messages

**Returns:** `Task` - Completes when message is enqueued

**Example:**
```csharp
await _outbox.EnqueueAsync(
    topic: "order.created",
    payload: JsonSerializer.Serialize(orderEvent),
    correlationId: orderId.ToString());
```

#### EnqueueAsync (Transactional)

Enqueues a message within an existing database transaction.

```csharp
Task EnqueueAsync(
    string topic,
    string payload,
    IDbTransaction transaction,
    string? correlationId = null)
```

**Parameters:**
- `topic` (string): The message topic for routing to handlers
- `payload` (string): The message content (typically JSON)
- `transaction` (IDbTransaction): The transaction to participate in
- `correlationId` (string?, optional): ID to correlate related messages

**Returns:** `Task` - Completes when message is enqueued

**Example:**
```csharp
using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
using var transaction = connection.BeginTransaction();

try
{
    // Business logic
    await SaveOrderAsync(order, transaction);
    
    // Enqueue in same transaction
    await _outbox.EnqueueAsync(
        "order.created",
        JsonSerializer.Serialize(order),
        transaction,
        order.Id.ToString());
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

#### ClaimAsync

Atomically claims ready messages for processing with a lease.

```csharp
Task<IReadOnlyList<Guid>> ClaimAsync(
    Guid ownerToken,
    int leaseSeconds,
    int batchSize,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `ownerToken` (Guid): Unique identifier for this worker process
- `leaseSeconds` (int): Duration to hold the lease (30-300 seconds typical)
- `batchSize` (int): Maximum number of messages to claim (1-100 typical)
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task<IReadOnlyList<Guid>>` - List of claimed message IDs

**Example:**
```csharp
var ownerToken = Guid.NewGuid();
var claimedIds = await _outbox.ClaimAsync(
    ownerToken: ownerToken,
    leaseSeconds: 30,
    batchSize: 50,
    cancellationToken: stoppingToken);
```

#### AckAsync

Acknowledges messages as successfully processed.

```csharp
Task AckAsync(
    Guid ownerToken,
    IEnumerable<Guid> ids,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `ownerToken` (Guid): The owner token used to claim the messages
- `ids` (IEnumerable<Guid>): IDs of messages to acknowledge
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when messages are acknowledged

**Example:**
```csharp
var successfulIds = new List<Guid>();

foreach (var id in claimedIds)
{
    if (await ProcessMessageAsync(id))
    {
        successfulIds.Add(id);
    }
}

await _outbox.AckAsync(ownerToken, successfulIds);
```

#### AbandonAsync

Returns messages to ready state for retry after temporary failure.

```csharp
Task AbandonAsync(
    Guid ownerToken,
    IEnumerable<Guid> ids,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `ownerToken` (Guid): The owner token used to claim the messages
- `ids` (IEnumerable<Guid>): IDs of messages to abandon
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when messages are abandoned

**Example:**
```csharp
var failedIds = new List<Guid>();

foreach (var id in claimedIds)
{
    try
    {
        await ProcessMessageAsync(id);
    }
    catch (TransientException)
    {
        failedIds.Add(id); // Retry later
    }
}

await _outbox.AbandonAsync(ownerToken, failedIds);
```

#### FailAsync

Marks messages as permanently failed.

```csharp
Task FailAsync(
    Guid ownerToken,
    IEnumerable<Guid> ids,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `ownerToken` (Guid): The owner token used to claim the messages
- `ids` (IEnumerable<Guid>): IDs of messages to mark as failed
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when messages are marked as failed

**Example:**
```csharp
try
{
    await ProcessMessageAsync(messageId);
}
catch (InvalidDataException)
{
    // Permanent error - don't retry
    await _outbox.FailAsync(ownerToken, new[] { messageId });
}
```

#### ReapExpiredAsync

Recovers messages with expired leases, returning them to ready state.

```csharp
Task ReapExpiredAsync(
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `cancellationToken` (CancellationToken, optional): Cancellation token

**Returns:** `Task` - Completes when expired messages are reaped

**Example:**
```csharp
// Called periodically by background service
await _outbox.ReapExpiredAsync(stoppingToken);
```

---

## IOutboxHandler Interface

Interface for implementing message handlers.

### Definition

```csharp
public interface IOutboxHandler
{
    /// <summary>
    /// The topic this handler processes.
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// Handles an outbox message.
    /// </summary>
    Task HandleAsync(
        OutboxMessage message,
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
public class OrderCreatedHandler : IOutboxHandler
{
    public string Topic => "order.created";
    
    // ...
}
```

### Methods

#### HandleAsync

Processes a message from the outbox.

```csharp
Task HandleAsync(
    OutboxMessage message,
    CancellationToken cancellationToken)
```

**Parameters:**
- `message` (OutboxMessage): The message to process
- `cancellationToken` (CancellationToken): Cancellation token

**Returns:** `Task` - Completes when message is handled

**Example:**
```csharp
public async Task HandleAsync(
    OutboxMessage message,
    CancellationToken cancellationToken)
{
    var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
    
    await _messageBroker.PublishAsync(
        "orders",
        message.Payload,
        cancellationToken);
    
    _logger.LogInformation(
        "Published order {OrderId} to message broker",
        orderEvent.OrderId);
}
```

---

## IOutboxRouter Interface

Interface for routing outbox operations to specific databases in multi-tenant scenarios.

### Methods

#### GetOutbox (String)

Gets the outbox for a specific routing key.

```csharp
IOutbox GetOutbox(string key)
```

**Parameters:**
- `key` (string): The routing key (e.g., tenant ID)

**Returns:** `IOutbox` - The outbox instance for this key

**Throws:**
- `ArgumentException` - If key is null or empty
- `InvalidOperationException` - If no outbox exists for the key

**Example:**
```csharp
public async Task CreateOrderAsync(string tenantId, Order order)
{
    var outbox = _outboxRouter.GetOutbox(tenantId);
    
    await outbox.EnqueueAsync(
        "order.created",
        JsonSerializer.Serialize(order),
        order.Id.ToString());
}
```

#### GetOutbox (Guid)

Gets the outbox for a specific GUID routing key.

```csharp
IOutbox GetOutbox(Guid key)
```

**Parameters:**
- `key` (Guid): The routing key (converted to string internally)

**Returns:** `IOutbox` - The outbox instance for this key

**Example:**
```csharp
var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var outbox = _outboxRouter.GetOutbox(customerId);
```

---

## OutboxMessage Class

Represents a message in the outbox.

### Properties

```csharp
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Topic { get; set; }
    public string Payload { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
```

**Property Descriptions:**
- `Id` - Unique message identifier
- `Topic` - Message topic for routing
- `Payload` - Message content (typically JSON)
- `CorrelationId` - Optional correlation identifier
- `CreatedAt` - When the message was created
- `RetryCount` - Number of processing attempts
- `LastError` - Error message from last failed attempt

---

## SqlOutboxOptions Class

Configuration options for SQL Server outbox.

### Properties

```csharp
public class SqlOutboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SchemaName { get; set; } = "infra";
    public string TableName { get; set; } = "Outbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

**Property Descriptions:**
- `ConnectionString` - SQL Server connection string (required)
- `SchemaName` - Database schema name (default: "infra")
- `TableName` - Outbox table name (default: "Outbox")
- `EnableSchemaDeployment` - Automatically create table and procedures (default: false)

### Example

```csharp
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "infra",
    TableName = "Outbox",
    EnableSchemaDeployment = true
});
```

---

## Service Registration Extensions

### AddSqlOutbox

Registers the SQL Server outbox service.

```csharp
public static IServiceCollection AddSqlOutbox(
    this IServiceCollection services,
    SqlOutboxOptions options)
```

**Parameters:**
- `services` - The service collection
- `options` - Configuration options

**Returns:** The service collection for chaining

**Example:**
```csharp
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;...",
    EnableSchemaDeployment = true
});
```

### AddMultiSqlOutbox

Registers multiple SQL Server outbox instances for multi-tenant scenarios.

```csharp
public static IServiceCollection AddMultiSqlOutbox(
    this IServiceCollection services,
    IEnumerable<SqlOutboxOptions> options)
```

**Parameters:**
- `services` - The service collection
- `options` - Collection of configuration options, one per database

**Returns:** The service collection for chaining

**Example:**
```csharp
var tenantDatabases = new[]
{
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant1;..."
    },
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant2;..."
    }
};

builder.Services.AddMultiSqlOutbox(tenantDatabases);
```

---

## Database Schema

### Outbox Table

```sql
CREATE TABLE infra.Outbox (
    -- Core message fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
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

CREATE INDEX IX_Outbox_WorkQueue ON infra.Outbox(Status, CreatedAt) 
    INCLUDE(Id, OwnerToken);
```

### Work Queue Stored Procedures

#### Outbox_Claim

Claims ready messages for processing.

```sql
EXEC infra.Outbox_Claim 
    @OwnerToken = '...',
    @LeaseSeconds = 30,
    @BatchSize = 50
```

#### Outbox_Ack

Acknowledges successful processing.

```sql
EXEC infra.Outbox_Ack 
    @OwnerToken = '...',
    @Ids = @IdList  -- User-defined table type
```

#### Outbox_Abandon

Returns messages to ready state.

```sql
EXEC infra.Outbox_Abandon 
    @OwnerToken = '...',
    @Ids = @IdList
```

#### Outbox_Fail

Marks messages as failed.

```sql
EXEC infra.Outbox_Fail 
    @OwnerToken = '...',
    @Ids = @IdList
```

#### Outbox_ReapExpired

Recovers expired leases.

```sql
EXEC infra.Outbox_ReapExpired
```

---

## Error Handling

### Common Exceptions

#### ArgumentException
Thrown when invalid parameters are provided.

```csharp
try
{
    await _outbox.EnqueueAsync(null, payload, correlationId);
}
catch (ArgumentException ex)
{
    // Handle invalid topic
}
```

#### InvalidOperationException
Thrown when attempting invalid operations (e.g., ack with wrong owner token).

```csharp
try
{
    await _outbox.AckAsync(wrongOwnerToken, ids);
}
catch (InvalidOperationException ex)
{
    // Handle ownership violation
}
```

#### SqlException
Thrown when database operations fail.

```csharp
try
{
    await _outbox.EnqueueAsync(topic, payload, correlationId);
}
catch (SqlException ex)
{
    // Handle database errors (connectivity, permissions, etc.)
}
```

---

## Best Practices

### 1. Use Appropriate Lease Duration

```csharp
// ✅ GOOD: Reasonable lease for expected processing time
await _outbox.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 50);

// ❌ BAD: Too short - might lose lease during processing
await _outbox.ClaimAsync(ownerToken, leaseSeconds: 5, batchSize: 50);

// ❌ BAD: Too long - slow recovery from crashes
await _outbox.ClaimAsync(ownerToken, leaseSeconds: 600, batchSize: 50);
```

### 2. Use Batching for Efficiency

```csharp
// ✅ GOOD: Process in batches
var claimed = await _outbox.ClaimAsync(ownerToken, 30, batchSize: 50);

// ❌ BAD: Processing one at a time is inefficient
var claimed = await _outbox.ClaimAsync(ownerToken, 30, batchSize: 1);
```

### 3. Handle Partial Success

```csharp
var successIds = new List<Guid>();
var failedIds = new List<Guid>();

foreach (var id in claimedIds)
{
    try
    {
        await ProcessAsync(id);
        successIds.Add(id);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process {Id}", id);
        failedIds.Add(id);
    }
}

// Ack successes and abandon failures separately
if (successIds.Any())
    await _outbox.AckAsync(ownerToken, successIds);
    
if (failedIds.Any())
    await _outbox.AbandonAsync(ownerToken, failedIds);
```

### 4. Implement Idempotent Handlers

```csharp
public class OrderCreatedHandler : IOutboxHandler
{
    public string Topic => "order.created";
    
    public async Task HandleAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        var order = JsonSerializer.Deserialize<OrderEvent>(message.Payload);
        
        // ✅ GOOD: Idempotent - check if already processed
        if (await _orderRepo.ExistsAsync(order.OrderId))
        {
            _logger.LogInformation("Order {OrderId} already processed", order.OrderId);
            return;
        }
        
        await _orderRepo.CreateAsync(order);
    }
}
```

---

## See Also

- [Outbox Quick Start](outbox-quickstart.md)
- [Outbox Examples](outbox-examples.md)
- [Multi-Outbox Guide](multi-outbox-guide.md)
- [Work Queue Pattern](work-queue-pattern.md)
