# Multi-Database Outbox Implementation

This implementation adds support for processing outbox messages across multiple databases (multi-tenant scenarios) using a single worker process.

## What's New

### Core Abstractions

- **`IOutboxStoreProvider`** - Provides access to multiple outbox stores (one per database)
- **`IOutboxSelectionStrategy`** - Pluggable strategy for selecting which outbox to poll next
- **`ConfiguredOutboxStoreProvider`** - Built-in provider for static database configuration
- **`MultiOutboxDispatcher`** - Dispatcher that processes messages across multiple outboxes
- **`MultiOutboxPollingService`** - Background service for continuous multi-outbox polling

### Selection Strategies

- **`RoundRobinOutboxSelectionStrategy`** - Cycles through all outboxes, ensuring fair distribution
- **`DrainFirstOutboxSelectionStrategy`** - Drains one outbox completely before moving to the next

## Quick Start

### Multiple Fixed Databases

```csharp
var outboxOptions = new[]
{
    new SqlOutboxOptions { ConnectionString = "Server=...;Database=Customer1;...", SchemaName = "dbo", TableName = "Outbox" },
    new SqlOutboxOptions { ConnectionString = "Server=...;Database=Customer2;...", SchemaName = "dbo", TableName = "Outbox" },
    new SqlOutboxOptions { ConnectionString = "Server=...;Database=Customer3;...", SchemaName = "dbo", TableName = "Outbox" },
};

services.AddMultiSqlOutbox(
    outboxOptions,
    selectionStrategy: new RoundRobinOutboxSelectionStrategy());

services.AddOutboxHandler<EmailOutboxHandler>();
```

### Dynamic Database Discovery

```csharp
services.AddMultiSqlOutbox(
    provider => new MyCustomStoreProvider(
        provider.GetRequiredService<IDatabaseRegistry>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILoggerFactory>()),
    selectionStrategy: new DrainFirstOutboxSelectionStrategy());
```

## Documentation

- **[Multi-Outbox Usage Guide](./docs/multi-outbox-guide.md)** - Complete guide with examples and best practices
- **[Multi-Database Pattern](./docs/multi-database-pattern.md)** - Architectural pattern and how to apply it to other primitives (inbox, leases)

## Backward Compatibility

All existing single-outbox functionality remains unchanged and fully supported:

```csharp
// Single outbox - works exactly as before
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyDb;...",
    SchemaName = "dbo",
    TableName = "Outbox",
});
```

## Tests

- **55 total outbox tests** - All passing
- **9 new multi-outbox tests** - Selection strategies and integration tests
- **7 selection strategy tests** - Unit tests for round-robin and drain-first strategies
- **2 integration tests** - Multi-database processing scenarios

Run tests:
```bash
dotnet test --filter "FullyQualifiedName~Outbox"
```

## Architecture

The multi-outbox implementation follows a clean, extensible architecture:

1. **Store Provider** manages the collection of outbox stores
2. **Selection Strategy** determines which store to poll next (pluggable)
3. **Dispatcher** coordinates message processing across stores
4. **Polling Service** runs continuously in the background

This pattern can be applied to other platform primitives (see [Multi-Database Pattern](./docs/multi-database-pattern.md)).

## Use Cases

- **Multi-tenant SaaS** - Process outbox messages from all customer databases
- **Sharded Databases** - Coordinate outbox processing across database shards
- **Regional Databases** - Process messages from databases in different regions
- **Priority Processing** - Use custom selection strategies to prioritize certain databases

## Performance

- Scales linearly with the number of databases
- Round-robin provides fair distribution across all customers
- Drain-first minimizes database connection overhead
- Configurable batch size and polling interval for tuning
