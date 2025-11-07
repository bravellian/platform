# Outbox Router Usage Guide

## Overview

The `IOutboxRouter` interface provides a mechanism for routing outbox write operations to the appropriate database in multi-tenant scenarios. This allows you to create outbox messages in the correct tenant database based on a routing key (e.g., tenant ID or customer ID).

## Use Cases

### Single Database Setup
For applications with a single database, use the standard `IOutbox` interface:

```csharp
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;",
    SchemaName = "dbo",
    TableName = "Outbox"
});

// Inject IOutbox directly
public class MyService
{
    private readonly IOutbox outbox;
    
    public MyService(IOutbox outbox)
    {
        this.outbox = outbox;
    }
    
    public async Task CreateOrderAsync(Order order)
    {
        await outbox.EnqueueAsync("order.created", JsonSerializer.Serialize(order), order.Id);
    }
}
```

### Multi-Database Setup with Static Configuration
For applications with a known set of tenant databases configured at startup:

```csharp
var tenantDatabases = new[]
{
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant1;",
        SchemaName = "dbo",
        TableName = "Outbox"
    },
    new SqlOutboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant2;",
        SchemaName = "dbo",
        TableName = "Outbox"
    }
};

services.AddMultiSqlOutbox(tenantDatabases);

// Inject IOutboxRouter
public class MyMultiTenantService
{
    private readonly IOutboxRouter outboxRouter;
    
    public MyMultiTenantService(IOutboxRouter outboxRouter)
    {
        this.outboxRouter = outboxRouter;
    }
    
    public async Task CreateOrderAsync(string tenantId, Order order)
    {
        // Get the outbox for this specific tenant
        var outbox = outboxRouter.GetOutbox(tenantId);
        
        // Enqueue message to the tenant's database
        await outbox.EnqueueAsync("order.created", JsonSerializer.Serialize(order), order.Id);
    }
}
```

### Multi-Database Setup with Dynamic Discovery
For applications where tenant databases are discovered at runtime:

```csharp
// First, implement the discovery interface
public class MyTenantDatabaseDiscovery : IOutboxDatabaseDiscovery
{
    private readonly IConfiguration configuration;
    
    public MyTenantDatabaseDiscovery(IConfiguration configuration)
    {
        this.configuration = configuration;
    }
    
    public async Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken)
    {
        // Query your tenant registry/database to get current tenants
        var tenants = await GetActiveTenantsAsync();
        
        return tenants.Select(t => new OutboxDatabaseConfig
        {
            Identifier = t.TenantId,
            ConnectionString = t.DatabaseConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        });
    }
}

// Register the discovery service and multi-outbox
services.AddSingleton<IOutboxDatabaseDiscovery, MyTenantDatabaseDiscovery>();
services.AddDynamicMultiSqlOutbox();

// Usage is the same as static configuration
public class MyService
{
    private readonly IOutboxRouter outboxRouter;
    
    public MyService(IOutboxRouter outboxRouter)
    {
        this.outboxRouter = outboxRouter;
    }
    
    public async Task CreateOrderAsync(string tenantId, Order order)
    {
        var outbox = outboxRouter.GetOutbox(tenantId);
        await outbox.EnqueueAsync("order.created", JsonSerializer.Serialize(order), order.Id);
    }
}
```

## Routing Key Types

The `IOutboxRouter` supports both string and GUID routing keys:

```csharp
// String-based routing (e.g., tenant name or ID)
var outbox = outboxRouter.GetOutbox("tenant-123");

// GUID-based routing (e.g., customer GUID)
var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var outbox = outboxRouter.GetOutbox(customerId);
```

Note: GUID keys are converted to strings internally, so the identifier registered in your outbox store provider must match the string representation of the GUID.

## Error Handling

The router will throw exceptions in the following cases:

- `ArgumentNullException`: If the routing key is null or empty
- `InvalidOperationException`: If no outbox exists for the specified routing key

```csharp
try
{
    var outbox = outboxRouter.GetOutbox(tenantId);
    await outbox.EnqueueAsync("order.created", payload, correlationId);
}
catch (InvalidOperationException ex)
{
    // Handle case where tenant database doesn't exist
    _logger.LogError(ex, "No outbox found for tenant {TenantId}", tenantId);
}
```

## Architecture Notes

### Read vs Write Operations
- **Read operations** (processing messages): Use `IOutboxStoreProvider` with `IOutboxSelectionStrategy` to determine which outbox to poll
- **Write operations** (creating messages): Use `IOutboxRouter` with a routing key to determine which outbox to write to

This separation ensures that:
1. Background workers can efficiently poll multiple databases using strategies like round-robin or drain-first
2. Application code can create messages in the correct tenant database based on context (e.g., tenant ID from the current request)

### Performance Considerations
- Outbox instances are cached by the providers, so repeated calls to `GetOutbox()` with the same key return the same instance
- Dynamic discovery providers refresh their database list periodically (default: 5 minutes) to detect new or removed tenants
