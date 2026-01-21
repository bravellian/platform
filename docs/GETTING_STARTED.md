# Getting Started with Bravellian Platform

Welcome! This guide will help you get up and running with the Bravellian Platform quickly.

## What is Bravellian Platform?

Bravellian Platform is a robust .NET library that provides reliable, production-ready implementations of common distributed system patterns:

- **Outbox Pattern** - Reliably publish messages alongside database transactions
- **Inbox Pattern** - Ensure idempotent message processing
- **Distributed Locking** - Coordinate exclusive operations across multiple instances
- **Work Queues** - Process tasks with claim-ack-abandon semantics
- **Time Abstractions** - Stable timing immune to system clock changes

## Installation

Add the package to your .NET project:

```bash
dotnet add package Bravellian.Platform
```

**Requirements:**
- .NET 6.0 or later
- SQL Server 2016 or later (or Azure SQL Database)

## Quick Start: Outbox Pattern

The outbox pattern ensures that database changes and message publishing happen atomically.

### 1. Configure Services

```csharp
using Bravellian.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true // Creates table automatically
});

var app = builder.Build();
app.Run();
```

### 2. Use in Your Code

```csharp
public class OrderService
{
    private readonly IOutbox _outbox;

    public async Task CreateOrderAsync(Order order, IDbTransaction transaction)
    {
        // Save order to database
        await SaveOrderToDatabase(order, transaction);

        // Enqueue message in same transaction
        await _outbox.EnqueueAsync(
            topic: "order.created",
            payload: JsonSerializer.Serialize(order),
            transaction: transaction,
            correlationId: order.Id.ToString());
    }
}
```

### 3. Create a Handler

```csharp
public class OrderCreatedHandler : IOutboxHandler
{
    public string Topic => "order.created";

    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        // Publish to your message broker, call APIs, etc.
        await _messageBroker.PublishAsync(message.Topic, message.Payload, ct);
    }
}

// Register the handler
builder.Services.AddTransient<IOutboxHandler, OrderCreatedHandler>();
```

**✅ Done!** Messages will be processed automatically in the background.

## Quick Start: Inbox Pattern

The inbox pattern prevents duplicate message processing.

### 1. Configure Services

```csharp
builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true
});
```

### 2. Use in Message Handlers

```csharp
public class WebhookController : ControllerBase
{
    private readonly IInbox _inbox;

    [HttpPost("webhooks/payment")]
    public async Task<IActionResult> PaymentWebhook([FromBody] PaymentEvent evt)
    {
        // Check if already processed
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId: evt.Id,
            source: "StripeWebhook");

        if (alreadyProcessed)
        {
            return Ok(); // Idempotent response
        }

        // Process the webhook
        await _inbox.MarkProcessingAsync(evt.Id);
        await ProcessPaymentAsync(evt);
        await _inbox.MarkProcessedAsync(evt.Id);

        return Ok();
    }
}
```

**✅ Done!** Duplicate webhooks will be safely ignored.

## Quick Start: Monotonic Clock

Use monotonic time for reliable timeouts and performance measurements.

### 1. Inject the Clock

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
}
```

### 2. Create Timeouts

```csharp
public async Task<ApiResponse> CallWithTimeoutAsync(string url, TimeSpan timeout)
{
    var deadline = MonoDeadline.In(_clock, timeout);

    while (!deadline.Expired(_clock))
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ApiResponse>(url);
        }
        catch (HttpRequestException) when (!deadline.Expired(_clock))
        {
            // Retry if we have time
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    throw new TimeoutException($"Request to {url} timed out");
}
```

**✅ Done!** Your timeouts are now immune to system clock changes.

## Architecture Overview

### How It Works

```
┌─────────────────┐
│  Your Code      │
│  (Transaction)  │
└────────┬────────┘
         │
         ├─► Save Business Data
         │
         └─► Enqueue Outbox Message
                │
                │ (Commit Transaction)
                ▼
         ┌──────────────┐
         │ Outbox Table │
         └──────┬───────┘
                │
                │ (Background Worker)
                ▼
         ┌──────────────┐
         │   Handler    │
         └──────┬───────┘
                │
                └─► Publish to Broker / Call API
```

**Key Concepts:**

1. **Work Queue Pattern** - Messages are "claimed" with a lease, processed, then acknowledged or abandoned
2. **Database-Authoritative** - SQL Server's `SYSUTCDATETIME()` is the source of truth
3. **At-Least-Once** - Messages will be retried until acknowledged
4. **Horizontal Scaling** - Multiple instances can process messages in parallel

### Database Tables

The platform creates tables automatically when `EnableSchemaDeployment = true`:

```
infra.Outbox              -- Outbound messages
infra.Inbox               -- Inbound message tracking
infra.Lease               -- Distributed locks
infra.Timers              -- Scheduled one-time tasks
infra.Jobs                -- Recurring job definitions
infra.JobRuns             -- Job execution instances
```

## Next Steps

### Learn the Patterns

- [Outbox Pattern Quick Start](outbox-quickstart.md) - Detailed guide with examples
- [Inbox Pattern Quick Start](inbox-quickstart.md) - Idempotent message processing
- [Monotonic Clock Guide](monotonic-clock-guide.md) - Stable timing for production

### API References

- [Outbox API Reference](outbox-api-reference.md) - Complete IOutbox documentation
- [Inbox API Reference](inbox-api-reference.md) - Complete IInbox documentation

### Advanced Topics

- [Multi-Tenant Patterns](multi-database-pattern.md) - Support multiple tenant databases
- [Outbox Router](OutboxRouter.md) - Route messages to specific databases
- [Inbox Router](InboxRouter.md) - Process messages from multiple databases
- [Distributed Locking](lease-v2-usage.md) - Lease system with auto-renewal

### Complete Documentation

Browse the [Documentation Index](INDEX.md) for all available guides and references.

## Common Questions

### When should I use the Outbox pattern?

Use the outbox when you need to:
- Publish messages reliably alongside database changes
- Ensure messages are sent even if publishing fails initially
- Decouple your business logic from message broker availability
- Implement the saga pattern or event-driven architecture

### When should I use the Inbox pattern?

Use the inbox when you need to:
- Process messages idempotently (exactly once)
- Handle duplicate message deliveries safely
- Prevent race conditions in message processing
- Track which messages have been processed

### When should I use Monotonic Clock?

Use monotonic clock for:
- Timeouts and deadlines
- Performance measurements
- Rate limiting
- Lease renewals
- Any timing that must be stable

**Don't use it for:**
- Database timestamps (use `TimeProvider`)
- Business logic dates (use `TimeProvider`)
- API responses (use `TimeProvider`)

### Can I use this in production?

**Yes!** The Bravellian Platform is designed for production use with:

✅ **Reliability** - Work queue pattern with automatic retry  
✅ **Scalability** - Horizontal scaling with multiple instances  
✅ **Testability** - Interfaces for dependency injection and testing  
✅ **Performance** - Optimized database queries and indexes  
✅ **Monitoring** - Health checks and logging built-in  

### What databases are supported?

Currently **SQL Server 2016+** and **Azure SQL Database**.

The platform uses:
- Standard SQL syntax
- Stored procedures for work queue operations
- User-defined table types for batch operations
- Work queue indexes for efficient querying

### Do I need a message broker?

**No!** The platform works standalone:

- Outbox messages are stored in SQL Server
- Background workers process messages from the database
- You can publish to a broker from your handlers if needed

This simplifies architecture and reduces dependencies while still providing reliability guarantees.

## Getting Help

- 📖 [Full Documentation](INDEX.md)
- 🐛 [Report Issues](https://github.com/bravellian/platform/issues)
- 💬 [Discussions](https://github.com/bravellian/platform/discussions)

## Example Application

Want to see it all working together? Check out our example applications:

```bash
git clone https://github.com/bravellian/platform
cd platform/samples
dotnet run
```

*(Coming soon)*

## What's Next?

Now that you understand the basics:

1. **Read the Quick Starts** - Deep dive into each pattern
2. **Explore the API References** - Learn all available methods
3. **Check the Examples** - See real-world usage patterns
4. **Build Something** - Start using the platform in your project!

---

**Welcome to the Bravellian Platform community!** 🎉

We're excited to see what you build with these patterns. If you have questions, feedback, or just want to share your experience, please reach out through our GitHub discussions.
