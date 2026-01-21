# Multi-Database Scheduler Support

The scheduler now supports both static and dynamic database configurations, enabling a single worker to process scheduler work from multiple customer databases.

## Features

- **Static Configuration**: Define a fixed list of databases to process
- **Dynamic Discovery**: Automatically detect new or removed databases at runtime
- **Per-Database Leasing**: Each database has its own lease, allowing multiple instances to run concurrently
- **Router Support**: Route write operations to the correct database based on routing keys
- **Pluggable Selection Strategy**: Control which database to process next

## Usage

### Single Database (Existing Behavior)

```csharp
services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;",
    SchemaName = "infra",
    JobsTableName = "Jobs",
    JobRunsTableName = "JobRuns",
    TimersTableName = "Timers",
});
```

### Multiple Databases (Static Configuration)

```csharp
var schedulerConfigs = new[]
{
    new SchedulerDatabaseConfig
    {
        Identifier = "Customer1",
        ConnectionString = "Server=localhost;Database=Customer1;",
        SchemaName = "infra",
    },
    new SchedulerDatabaseConfig
    {
        Identifier = "Customer2",
        ConnectionString = "Server=localhost;Database=Customer2;",
        SchemaName = "infra",
    },
};

services.AddMultiSqlScheduler(schedulerConfigs);

// Also need to register a lease factory for one of the databases
services.AddSystemLeases(new SystemLeaseOptions
{
    ConnectionString = "Server=localhost;Database=Customer1;",
    SchemaName = "infra",
});
```

### Multiple Databases (Dynamic Discovery)

```csharp
// First, implement the discovery interface
public class MySchedulerDatabaseDiscovery : ISchedulerDatabaseDiscovery
{
    private readonly string registryConnectionString;
    
    public MySchedulerDatabaseDiscovery(string registryConnectionString)
    {
        this.registryConnectionString = registryConnectionString;
    }
    
    public async Task<IEnumerable<SchedulerDatabaseConfig>> DiscoverDatabasesAsync(
        CancellationToken cancellationToken = default)
    {
        // Query your registry database to get the list of active customer databases
        // This is called periodically to detect changes
        var databases = new List<SchedulerDatabaseConfig>();
        
        using var connection = new SqlConnection(this.registryConnectionString);
        await connection.OpenAsync(cancellationToken);
        
        var customers = await connection.QueryAsync<(string Id, string ConnectionString)>(
            "SELECT CustomerId, ConnectionString FROM ActiveCustomers");
            
        foreach (var customer in customers)
        {
            databases.Add(new SchedulerDatabaseConfig
            {
                Identifier = customer.Id,
                ConnectionString = customer.ConnectionString,
                SchemaName = "infra",
            });
        }
        
        return databases;
    }
}

// Then register it
services.AddSingleton<ISchedulerDatabaseDiscovery>(
    new MySchedulerDatabaseDiscovery("Server=localhost;Database=Registry;"));

services.AddDynamicMultiSqlScheduler(
    refreshInterval: TimeSpan.FromMinutes(5));

// Also need to register a lease factory
services.AddSystemLeases(new SystemLeaseOptions
{
    ConnectionString = "Server=localhost;Database=Registry;",
    SchemaName = "infra",
});
```

### Using the Scheduler Router

When working with multiple databases, use the router to direct scheduler operations to the correct database:

```csharp
public class MyController : Controller
{
    private readonly ISchedulerRouter schedulerRouter;
    
    public MyController(ISchedulerRouter schedulerRouter)
    {
        this.schedulerRouter = schedulerRouter;
    }
    
    public async Task<IActionResult> ScheduleJob(string customerId)
    {
        // Get the scheduler client for this customer
        var schedulerClient = this.schedulerRouter.GetSchedulerClient(customerId);
        
        // Schedule a job in the customer's database
        await schedulerClient.CreateOrUpdateJobAsync(
            jobName: "daily-report",
            topic: "generate-daily-report",
            cronSchedule: "0 0 * * *", // Daily at midnight
            payload: JsonSerializer.Serialize(new { CustomerId = customerId }));
            
        return Ok();
    }
}
```

## How It Works

1. **Store Provider**: Manages the list of database stores and provides access to them
2. **Multi-Scheduler Dispatcher**: Processes work from multiple databases using a selection strategy
3. **Multi-Scheduler Polling Service**: Background service that periodically runs the dispatcher
4. **Per-Database Leasing**: Each database has its own lease key (e.g., `scheduler:run:Customer1`), allowing:
   - Multiple instances to run concurrently, each processing different databases
   - A single database to be processed by only one instance at a time
   - Automatic failover if an instance fails

## Architecture

The multi-database scheduler follows the same pattern as the multi-database outbox:

```
┌─────────────────────────────────────────────────────────────┐
│                  MultiSchedulerPollingService               │
│                  (Background Service)                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           │ polls every 30s
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                  MultiSchedulerDispatcher                   │
│                  (Selection Strategy)                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           │ selects database
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│              ISchedulerStoreProvider                        │
│              (Configured or Dynamic)                        │
└────┬────────────────────┬────────────────────┬─────────────┘
     │                    │                    │
     ▼                    ▼                    ▼
┌─────────┐         ┌─────────┐         ┌─────────┐
│Customer1│         │Customer2│         │Customer3│
│Scheduler│         │Scheduler│         │Scheduler│
│Store    │         │Store    │         │Store    │
└─────────┘         └─────────┘         └─────────┘
```

Each database is processed independently with its own lease, ensuring:
- Work is distributed across instances
- No database is processed by multiple instances simultaneously
- Failed instances don't block processing of other databases
