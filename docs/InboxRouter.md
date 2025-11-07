# Inbox Router Usage Guide

## Overview

The `IInboxRouter` interface provides a mechanism for routing inbox write operations to the appropriate database in multi-tenant scenarios. This allows you to enqueue messages in the correct tenant database based on a routing key (e.g., tenant ID or customer ID).

## Use Cases

### Single Database Setup
For applications with a single database, use the standard `IInbox` interface:

```csharp
services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;",
    SchemaName = "dbo",
    TableName = "Inbox"
});

// Inject IInbox directly
public class MyService
{
    private readonly IInbox inbox;
    
    public MyService(IInbox inbox)
    {
        this.inbox = inbox;
    }
    
    public async Task ProcessOrderAsync(Order order)
    {
        await inbox.EnqueueAsync("order.process", "external-api", order.Id, JsonSerializer.Serialize(order));
    }
}
```

### Multi-Database Setup with Static Configuration
For applications with a known set of tenant databases configured at startup:

```csharp
var tenantDatabases = new[]
{
    new SqlInboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant1;",
        SchemaName = "dbo",
        TableName = "Inbox"
    },
    new SqlInboxOptions
    {
        ConnectionString = "Server=localhost;Database=Tenant2;",
        SchemaName = "dbo",
        TableName = "Inbox"
    }
};

services.AddMultiSqlInbox(tenantDatabases);

// Inject IInboxRouter
public class MyMultiTenantService
{
    private readonly IInboxRouter inboxRouter;
    
    public MyMultiTenantService(IInboxRouter inboxRouter)
    {
        this.inboxRouter = inboxRouter;
    }
    
    public async Task ProcessOrderAsync(string tenantId, Order order)
    {
        // Get the inbox for this specific tenant
        var inbox = inboxRouter.GetInbox(tenantId);
        
        // Enqueue message to the tenant's database
        await inbox.EnqueueAsync("order.process", "external-api", order.Id, JsonSerializer.Serialize(order));
    }
}
```

### Multi-Database Setup with Dynamic Discovery
For applications where tenant databases are discovered at runtime:

```csharp
// First, implement the discovery interface
public class MyTenantDatabaseDiscovery : IInboxDatabaseDiscovery
{
    private readonly IConfiguration configuration;
    
    public MyTenantDatabaseDiscovery(IConfiguration configuration)
    {
        this.configuration = configuration;
    }
    
    public async Task<IEnumerable<InboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken)
    {
        // Query your tenant registry/database to get current tenants
        var tenants = await GetActiveTenantsAsync();
        
        return tenants.Select(t => new InboxDatabaseConfig
        {
            Identifier = t.TenantId,
            ConnectionString = t.DatabaseConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox"
        });
    }
}

// Register the discovery service and multi-inbox
services.AddSingleton<IInboxDatabaseDiscovery, MyTenantDatabaseDiscovery>();
services.AddDynamicMultiSqlInbox();

// Usage is the same as static configuration
public class MyService
{
    private readonly IInboxRouter inboxRouter;
    
    public MyService(IInboxRouter inboxRouter)
    {
        this.inboxRouter = inboxRouter;
    }
    
    public async Task ProcessOrderAsync(string tenantId, Order order)
    {
        var inbox = inboxRouter.GetInbox(tenantId);
        await inbox.EnqueueAsync("order.process", "external-api", order.Id, JsonSerializer.Serialize(order));
    }
}
```

## Routing Key Types

The `IInboxRouter` supports both string and GUID routing keys:

```csharp
// String-based routing (e.g., tenant name or ID)
var inbox = inboxRouter.GetInbox("tenant-123");

// GUID-based routing (e.g., customer GUID)
var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var inbox = inboxRouter.GetInbox(customerId);
```

Note: GUID keys are converted to strings internally, so the identifier registered in your inbox work store provider must match the string representation of the GUID.

## Error Handling

The router will throw exceptions in the following cases:

- `ArgumentException`: If the routing key is null, empty, or whitespace
- `InvalidOperationException`: If no inbox exists for the specified routing key

```csharp
try
{
    var inbox = inboxRouter.GetInbox(tenantId);
    await inbox.EnqueueAsync("order.process", "external-api", orderId, payload);
}
catch (InvalidOperationException ex)
{
    // Handle case where tenant database doesn't exist
    _logger.LogError(ex, "No inbox found for tenant {TenantId}", tenantId);
}
```

## Architecture Notes

### Read vs Write Operations
- **Read operations** (processing messages): Use `IInboxWorkStoreProvider` with `IInboxSelectionStrategy` to determine which inbox to poll
- **Write operations** (enqueueing messages): Use `IInboxRouter` with a routing key to determine which inbox to write to

This separation ensures that:
1. Background workers can efficiently poll multiple databases using strategies like round-robin or drain-first
2. Application code can enqueue messages in the correct tenant database based on context (e.g., tenant ID from the current request)

### Performance Considerations
- Inbox instances are cached by the providers, so repeated calls to `GetInbox()` with the same key return the same instance
- Dynamic discovery providers refresh their database list periodically (default: 5 minutes) to detect new or removed tenants

## Integration with Outbox

The inbox router pattern mirrors the outbox router pattern for consistency:

- **Outbox**: For sending messages FROM your application TO other systems
  - Use `IOutboxRouter` to route writes based on tenant
  - Messages are processed and sent to external systems

- **Inbox**: For receiving messages FROM external systems INTO your application
  - Use `IInboxRouter` to route writes based on tenant
  - Messages are deduplicated and processed by your handlers

Both patterns support the same multi-tenant scenarios and use similar routing mechanisms.
