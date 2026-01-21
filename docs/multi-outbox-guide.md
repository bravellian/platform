# Multi-Outbox Processing Guide

## Overview

The multi-outbox functionality enables processing outbox messages across multiple databases (e.g., one per customer/tenant) using a single worker process. This is critical for multi-tenant applications where each customer has their own database with their own outbox table.

## Key Concepts

### Architecture

The multi-outbox system is built on four key abstractions:

1. **`IOutboxStoreProvider`** - Provides access to all outbox stores that should be processed
2. **`IOutboxSelectionStrategy`** - Determines which outbox to poll next
3. **`MultiOutboxDispatcher`** - Dispatches messages from the selected outbox
4. **`MultiOutboxPollingService`** - Background service that continuously polls and processes messages

### Selection Strategies

Two built-in strategies are provided:

#### Round-Robin Strategy
Cycles through all outboxes in order, processing one batch from each before moving to the next.

**Use when:** You want fair distribution of processing across all databases.

```csharp
var strategy = new RoundRobinOutboxSelectionStrategy();
```

#### Drain-First Strategy
Continues processing from the same outbox until it returns no messages, then moves to the next.

**Use when:** You want to completely drain one database before moving to the next.

```csharp
var strategy = new DrainFirstOutboxSelectionStrategy();
```

## Usage

### Basic Setup (Multiple Fixed Databases)

When you have a known set of databases at startup:

```csharp
using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;

// Define your outbox databases
var outboxOptions = new[]
{
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Customer1;...",
        SchemaName = "infra",
        TableName = "Outbox",
    },
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Customer2;...",
        SchemaName = "infra",
        TableName = "Outbox",
    },
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Customer3;...",
        SchemaName = "infra",
        TableName = "Outbox",
    },
};

// Register multi-outbox services
services.AddMultiSqlOutbox(
    outboxOptions,
    selectionStrategy: new RoundRobinOutboxSelectionStrategy());

// Register your handlers
services.AddOutboxHandler<EmailOutboxHandler>();
services.AddOutboxHandler<NotificationOutboxHandler>();
```

### Advanced Setup (Dynamic Database Discovery)

When databases are discovered at runtime (e.g., from a global database or configuration service):

#### Option 1: Using the Built-in Dynamic Provider (Recommended)

The framework provides a `DynamicOutboxStoreProvider` that automatically detects new or removed databases. You only need to implement the `IOutboxDatabaseDiscovery` interface:

```csharp
// 1. Implement the discovery interface
public class GlobalDatabaseOutboxDiscovery : IOutboxDatabaseDiscovery
{
    private readonly string globalConnectionString;

    public GlobalDatabaseOutboxDiscovery(IConfiguration configuration)
    {
        this.globalConnectionString = configuration.GetConnectionString("GlobalDatabase");
    }

    public async Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        // Query your global database for active customers
        using var connection = new SqlConnection(this.globalConnectionString);
        await connection.OpenAsync(cancellationToken);

        var customers = await connection.QueryAsync<CustomerDatabase>(
            "SELECT CustomerId, ConnectionString FROM Customers WHERE IsActive = 1");

        return customers.Select(c => new OutboxDatabaseConfig
        {
            Identifier = c.CustomerId,
            ConnectionString = c.ConnectionString,
            SchemaName = "infra",
            TableName = "Outbox",
        });
    }
}

// 2. Register the discovery service and dynamic provider
services.AddSingleton<IOutboxDatabaseDiscovery, GlobalDatabaseOutboxDiscovery>();
services.AddDynamicMultiSqlOutbox(
    selectionStrategy: new RoundRobinOutboxSelectionStrategy(),
    refreshInterval: TimeSpan.FromMinutes(5)); // Checks for new/removed databases every 5 minutes

// 3. Register your handlers
services.AddOutboxHandler<EmailOutboxHandler>();
```

The `DynamicOutboxStoreProvider` will:
- ✅ Query the discovery service periodically (default: 5 minutes)
- ✅ Automatically create stores for new databases
- ✅ Remove stores for databases that no longer exist
- ✅ Update stores if database configuration changes
- ✅ Manage store lifecycle efficiently (no unnecessary recreations)

#### Option 2: Custom Store Provider

For advanced scenarios, you can create a fully custom provider:

```csharp
// Create a custom store provider implementation
public class CustomOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly ICustomerDatabaseRegistry registry;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly List<IOutboxStore> stores = new();
    private readonly Dictionary<IOutboxStore, string> identifiers = new();

    public CustomOutboxStoreProvider(
        ICustomerDatabaseRegistry registry,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        this.registry = registry;
        this.timeProvider = timeProvider;
        this.loggerFactory = loggerFactory;
        this.RefreshStores();
    }

    public IReadOnlyList<IOutboxStore> GetAllStores()
    {
        // Optionally refresh the list of databases periodically
        this.RefreshStores();
        return this.stores;
    }

    public string GetStoreIdentifier(IOutboxStore store)
    {
        return this.identifiers.TryGetValue(store, out var id) ? id : "Unknown";
    }

    private void RefreshStores()
    {
        this.stores.Clear();
        this.identifiers.Clear();

        var databases = this.registry.GetAllCustomerDatabases();
        var logger = this.loggerFactory.CreateLogger<SqlOutboxStore>();

        foreach (var db in databases)
        {
            var store = new SqlOutboxStore(
                Options.Create(new SqlOutboxOptions
                {
                    ConnectionString = db.ConnectionString,
                    SchemaName = "infra",
                    TableName = "Outbox",
                }),
                this.timeProvider,
                logger);

            this.stores.Add(store);
            this.identifiers[store] = db.CustomerName;
        }
    }
}

// Register with custom provider
services.AddMultiSqlOutbox(
    provider => new CustomOutboxStoreProvider(
        provider.GetRequiredService<ICustomerDatabaseRegistry>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILoggerFactory>()),
    selectionStrategy: new DrainFirstOutboxSelectionStrategy());
```

### Creating Custom Selection Strategies

Implement `IOutboxSelectionStrategy` for custom logic:

```csharp
public class PriorityBasedSelectionStrategy : IOutboxSelectionStrategy
{
    private readonly Dictionary<IOutboxStore, int> priorities;
    private int currentIndex = 0;

    public PriorityBasedSelectionStrategy(Dictionary<IOutboxStore, int> priorities)
    {
        this.priorities = priorities;
    }

    public IOutboxStore? SelectNext(
        IReadOnlyList<IOutboxStore> stores,
        IOutboxStore? lastProcessedStore,
        int lastProcessedCount)
    {
        if (stores.Count == 0) return null;

        // Select the store with the highest priority that hasn't been processed recently
        var sortedStores = stores
            .OrderByDescending(s => this.priorities.GetValueOrDefault(s, 0))
            .ToList();

        this.currentIndex = (this.currentIndex + 1) % sortedStores.Count;
        return sortedStores[this.currentIndex];
    }

    public void Reset()
    {
        this.currentIndex = 0;
    }
}
```

## Handler Implementation

Handlers work the same way for both single and multi-outbox setups:

```csharp
public class EmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailService emailService;
    private readonly ILogger<EmailOutboxHandler> logger;

    public EmailOutboxHandler(IEmailService emailService, ILogger<EmailOutboxHandler> logger)
    {
        this.emailService = emailService;
        this.logger = logger;
    }

    public string Topic => "Email.Send";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var emailData = JsonSerializer.Deserialize<EmailData>(message.Payload);
        
        this.logger.LogInformation(
            "Sending email to {Recipient} for message {MessageId}",
            emailData.To,
            message.Id);

        await this.emailService.SendAsync(emailData, cancellationToken);
    }
}
```

## Backward Compatibility

The existing single-outbox setup remains unchanged and fully supported:

```csharp
// Single outbox - works exactly as before
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyDb;...",
    SchemaName = "infra",
    TableName = "Outbox",
});

services.AddOutboxHandler<EmailOutboxHandler>();
```

## Configuration Options

### Polling Interval

You can customize the polling interval and batch size when registering the service:

```csharp
services.AddSingleton<MultiOutboxPollingService>(provider =>
    new MultiOutboxPollingService(
        provider.GetRequiredService<MultiOutboxDispatcher>(),
        provider.GetRequiredService<IMonotonicClock>(),
        provider.GetRequiredService<ILogger<MultiOutboxPollingService>>(),
        intervalSeconds: 0.5,  // Poll every 500ms
        batchSize: 100,        // Process up to 100 messages per batch
        provider.GetService<IDatabaseSchemaCompletion>()));
```

### Backoff Policy

The dispatcher uses the same backoff policy as the single-outbox dispatcher:

```csharp
services.AddSingleton<MultiOutboxDispatcher>(provider =>
    new MultiOutboxDispatcher(
        provider.GetRequiredService<IOutboxStoreProvider>(),
        provider.GetRequiredService<IOutboxSelectionStrategy>(),
        provider.GetRequiredService<IOutboxHandlerResolver>(),
        provider.GetRequiredService<ILogger<MultiOutboxDispatcher>>(),
        backoffPolicy: OutboxDispatcher.DefaultBackoff)); // Or your custom policy
```

## Monitoring and Diagnostics

The multi-outbox dispatcher logs the store identifier with each operation:

```
[Information] Processing 5 outbox messages from store 'Customer1'
[Information] Completed outbox batch processing from store 'Customer1': 5/5 messages processed
[Information] Processing 3 outbox messages from store 'Customer2'
```

This makes it easy to track which database is being processed and identify any issues.

## Performance Considerations

1. **Batch Size**: Larger batches reduce database round-trips but increase processing time
2. **Polling Interval**: Shorter intervals reduce latency but increase CPU/database load
3. **Number of Stores**: Performance scales linearly with the number of databases
4. **Selection Strategy**: Round-robin provides fairness; drain-first minimizes database connections

## Migration from Single to Multi-Outbox

1. **Keep existing single-outbox registration** for your primary database
2. **Add multi-outbox registration** for additional databases
3. **Use different handlers** if needed, or share handlers across all outboxes
4. **Monitor logs** to ensure all databases are being processed

Example migration:

```csharp
// Before: Single outbox
services.AddSqlOutbox(connectionString: "...");

// After: Multi-outbox
var outboxOptions = GetAllCustomerDatabaseOptions();
services.AddMultiSqlOutbox(outboxOptions);
```

## Best Practices

1. **Use Round-Robin for fairness** across all customers
2. **Use Drain-First for priority customers** or when minimizing connection overhead
3. **Implement custom strategies** for complex business logic (e.g., SLA-based processing)
4. **Monitor per-database metrics** to identify bottlenecks
5. **Scale horizontally** by running multiple workers with the same configuration
