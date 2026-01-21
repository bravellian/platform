# Dynamic Outbox Discovery - Example Implementation

This document shows a complete example of implementing dynamic outbox discovery using a global database to track customer databases.

## Scenario

You have:
- A **global database** that stores information about all customers
- Multiple **customer databases**, one per customer
- Each customer database has its own `Outbox` table
- Customers can be added or removed at any time

## Implementation

### 1. Database Schema (Global Database)

```sql
-- Table to track customer databases
CREATE TABLE Customers (
    CustomerId NVARCHAR(100) PRIMARY KEY,
    CustomerName NVARCHAR(255) NOT NULL,
    ConnectionString NVARCHAR(1000) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

-- Example data
INSERT INTO Customers (CustomerId, CustomerName, ConnectionString, IsActive)
VALUES 
    ('customer-001', 'Acme Corp', 'Server=localhost;Database=AcmeCorpDB;...', 1),
    ('customer-002', 'Globex Inc', 'Server=localhost;Database=GlobexDB;...', 1),
    ('customer-003', 'Initech', 'Server=localhost;Database=InitechDB;...', 1);
```

### 2. Discovery Implementation

```csharp
using Bravellian.Platform;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class GlobalDatabaseOutboxDiscovery : IOutboxDatabaseDiscovery
{
    private readonly string globalConnectionString;
    private readonly ILogger<GlobalDatabaseOutboxDiscovery> logger;

    public GlobalDatabaseOutboxDiscovery(
        IConfiguration configuration,
        ILogger<GlobalDatabaseOutboxDiscovery> logger)
    {
        this.globalConnectionString = configuration.GetConnectionString("GlobalDatabase") 
            ?? throw new InvalidOperationException("GlobalDatabase connection string not found");
        this.logger = logger;
    }

    public async Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            this.logger.LogDebug("Querying global database for active customer databases");

            await using var connection = new SqlConnection(this.globalConnectionString);
            await connection.OpenAsync(cancellationToken);

            var customers = await connection.QueryAsync<CustomerRecord>(
                @"SELECT CustomerId, CustomerName, ConnectionString 
                  FROM Customers 
                  WHERE IsActive = 1",
                cancellationToken);

            var configs = customers.Select(c => new OutboxDatabaseConfig
            {
                Identifier = c.CustomerId,
                ConnectionString = c.ConnectionString,
                SchemaName = "infra",
                TableName = "Outbox",
            }).ToList();

            this.logger.LogInformation(
                "Discovered {Count} active customer databases",
                configs.Count);

            return configs;
        }
        catch (Exception ex)
        {
            this.logger.LogError(
                ex,
                "Error discovering customer databases from global database");
            
            // Return empty list on error to prevent disrupting existing processing
            return Enumerable.Empty<OutboxDatabaseConfig>();
        }
    }

    private class CustomerRecord
    {
        public string CustomerId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
    }
}
```

### 3. Registration in Startup

```csharp
using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register the discovery service
        services.AddSingleton<IOutboxDatabaseDiscovery, GlobalDatabaseOutboxDiscovery>();

        // Register dynamic multi-outbox with 5-minute refresh interval
        services.AddDynamicMultiSqlOutbox(
            selectionStrategy: new RoundRobinOutboxSelectionStrategy(),
            refreshInterval: TimeSpan.FromMinutes(5));

        // Register outbox handlers
        services.AddOutboxHandler<SendEmailHandler>();
        services.AddOutboxHandler<GenerateReportHandler>();
        services.AddOutboxHandler<SendNotificationHandler>();
    }
}
```

### 4. Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "GlobalDatabase": "Server=localhost;Database=GlobalDB;Integrated Security=true;TrustServerCertificate=true"
  },
  "Logging": {
    "LogLevel": {
      "Bravellian.Platform.DynamicOutboxStoreProvider": "Information",
      "Bravellian.Platform.MultiOutboxDispatcher": "Information"
    }
  }
}
```

## How It Works

1. **Initial Discovery**
   - On startup, `DynamicOutboxStoreProvider` calls your discovery service
   - Creates an `IOutboxStore` for each discovered database
   - Starts processing messages

2. **Periodic Refresh**
   - Every 5 minutes (configurable), the provider checks for changes
   - Queries the global database for active customers
   - Adds stores for new customers
   - Removes stores for deleted/inactive customers

3. **Message Processing**
   - `MultiOutboxDispatcher` uses the selection strategy to pick the next database
   - Claims messages from that database
   - Executes the appropriate handler
   - Marks messages as dispatched or reschedules them

## Adding a New Customer

When you add a new customer to the global database:

```sql
INSERT INTO Customers (CustomerId, CustomerName, ConnectionString, IsActive)
VALUES ('customer-004', 'New Customer', 'Server=localhost;Database=NewCustomerDB;...', 1);
```

Within 5 minutes (the refresh interval), the system will:
1. Discover the new customer database
2. Create an outbox store for it
3. Start processing messages from that database

## Removing a Customer

When you deactivate a customer:

```sql
UPDATE Customers 
SET IsActive = 0 
WHERE CustomerId = 'customer-002';
```

Within 5 minutes, the system will:
1. Detect that the customer is no longer active
2. Remove the outbox store
3. Stop processing messages from that database

## Advanced: Caching for Performance

If querying the global database is expensive, you can add caching:

```csharp
public class CachedGlobalDatabaseDiscovery : IOutboxDatabaseDiscovery
{
    private readonly GlobalDatabaseOutboxDiscovery inner;
    private readonly IMemoryCache cache;
    private const string CacheKey = "CustomerDatabases";

    public CachedGlobalDatabaseDiscovery(
        GlobalDatabaseOutboxDiscovery inner,
        IMemoryCache cache)
    {
        this.inner = inner;
        this.cache = cache;
    }

    public async Task<IEnumerable<OutboxDatabaseConfig>> DiscoverDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        if (this.cache.TryGetValue(CacheKey, out IEnumerable<OutboxDatabaseConfig>? cached))
        {
            return cached!;
        }

        var configs = await this.inner.DiscoverDatabasesAsync(cancellationToken);
        var configList = configs.ToList();

        this.cache.Set(
            CacheKey,
            configList,
            TimeSpan.FromMinutes(2)); // Cache for 2 minutes

        return configList;
    }
}
```

## Monitoring

The dynamic provider logs important events:

```
[Information] Discovered new outbox database: customer-004
[Information] Outbox database removed: customer-002
[Information] Discovery complete. Managing 3 outbox databases
[Information] Processing 5 outbox messages from store 'customer-001'
```

Monitor these logs to track:
- When new databases are discovered
- When databases are removed
- Any discovery errors
- Processing activity per customer

## Testing

You can test the dynamic discovery without a real global database:

```csharp
// Create test discovery
var testDiscovery = new SampleOutboxDatabaseDiscovery(new[]
{
    new OutboxDatabaseConfig
    {
        Identifier = "test-customer-1",
        ConnectionString = testConnectionString1,
    },
    new OutboxDatabaseConfig
    {
        Identifier = "test-customer-2",
        ConnectionString = testConnectionString2,
    },
});

// Register for testing
services.AddSingleton<IOutboxDatabaseDiscovery>(testDiscovery);
services.AddDynamicMultiSqlOutbox();
```
