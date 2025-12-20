# Schema Configuration Guide

This guide explains how to configure and use custom database schemas with the Bravellian Platform.

## Overview

By default, all platform components use the `dbo` schema. However, you can configure each component to use a custom schema for better organization, security, or compliance requirements.

## Configuration

### SQL Scheduler

```csharp
services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "scheduler",        // Use 'scheduler' schema instead of 'dbo'
    EnableSchemaDeployment = true,
    MaxPollingInterval = TimeSpan.FromSeconds(30),
    EnableBackgroundWorkers = true
});
```

This will create the following objects in the `scheduler` schema:
- Tables: `Jobs`, `JobRuns`, `Timers`, `SchedulerState`
- Stored procedures: `Timers_Claim`, `Timers_Ack`, `Timers_Abandon`, `Timers_ReapExpired`, `JobRuns_Claim`, `JobRuns_Ack`, `JobRuns_Abandon`, `JobRuns_ReapExpired`
- User-defined types: `GuidIdList`

### SQL Outbox

```csharp
services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "messaging",        // Use 'messaging' schema
    TableName = "Outbox",
    EnableSchemaDeployment = true
});
```

This will create the following objects in the `messaging` schema:
- Tables: `Outbox`, `OutboxState`
- Stored procedures: `Outbox_Claim`, `Outbox_Ack`, `Outbox_Abandon`, `Outbox_Fail`, `Outbox_ReapExpired`
- User-defined types: `GuidIdList`

### SQL Inbox

```csharp
services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "messaging",        // Use 'messaging' schema
    TableName = "Inbox",
    EnableSchemaDeployment = true
});
```

This will create the following objects in the `messaging` schema:
- Tables: `Inbox`
- Stored procedures: `Inbox_Claim`, `Inbox_Ack`, `Inbox_Abandon`, `Inbox_Fail`, `Inbox_ReapExpired`
- User-defined types: `StringIdList`

### System Leases (Distributed Locks)

```csharp
services.AddSystemLeases(new SystemLeaseOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "locks",            // Use 'locks' schema
    EnableSchemaDeployment = true
});
```

This will create the following objects in the `locks` schema:
- Tables: `Lease`, `DistributedLock`
- Stored procedures: `Lock_Acquire`, `Lock_Renew`, `Lock_Release`, `Lock_CleanupExpired`, `Lease_Acquire`, `Lease_Renew`

### Fanout System

```csharp
services.AddSqlFanout(new SqlFanoutOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    SchemaName = "fanout",           // Use 'fanout' schema
    PolicyTableName = "Policy",
    CursorTableName = "Cursor",
    EnableSchemaDeployment = true
});
```

This will create the following objects in the `fanout` schema:
- Tables: `FanoutPolicy`, `FanoutCursor`

## Schema Creation

### Automatic Creation

When `EnableSchemaDeployment = true`, the platform will automatically:
1. Create the schema if it doesn't exist
2. Create all required tables, indexes, and constraints
3. Create all stored procedures and user-defined types

This happens during application startup via the `DatabaseSchemaBackgroundService`.

### Manual Creation

If you prefer to manage schema creation manually (e.g., through database migration tools), set `EnableSchemaDeployment = false` and create the schema beforehand:

```sql
-- Create the schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'scheduler')
BEGIN
    EXEC('CREATE SCHEMA scheduler');
END
GO

-- Then deploy platform objects using DatabaseSchemaManager
```

Or use the `DatabaseSchemaManager` class directly:

```csharp
await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
    connectionString,
    schemaName: "scheduler",
    jobsTableName: "Jobs",
    jobRunsTableName: "JobRuns",
    timersTableName: "Timers");
```

## Schema Drift & Upgrade Guidance

The repository tracks a lightweight manifest of the expected schema hashes for the core modules (outbox, inbox, scheduler, fanout) in `src/Bravellian.Platform.Database/schema-versions.json`. When schema-defining code changes, regenerate and review this manifest to ensure upgrades are intentional:

1. Refresh the manifest and show the diff:
   ```bash
   ./scripts/schema-diff.sh
   ```
   This command hashes the current create scripts (no database required), rewrites `schema-versions.json`, and prints the git diff.
2. Review the changes. If they reflect the expected upgrade path, commit the updated manifest along with the code changes.
3. Downstream deployments can use the manifest to verify that all modules were upgraded together and to spot drift between environments.

Tip: You can also regenerate the manifest in isolation with:
```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test tests/Bravellian.Platform.Tests/Bravellian.Platform.Tests.csproj --filter SchemaVersions_MatchSnapshot
```


## Best Practices

### 1. Use Schemas for Logical Separation

```csharp
// Separate schemas for different concerns
services.AddSqlScheduler(new SqlSchedulerOptions 
{ 
    SchemaName = "scheduler" 
});

services.AddSqlOutbox(new SqlOutboxOptions 
{ 
    SchemaName = "messaging" 
});

services.AddSqlInbox(new SqlInboxOptions 
{ 
    SchemaName = "messaging" 
});

services.AddSystemLeases(new SystemLeaseOptions 
{ 
    SchemaName = "locks" 
});
```

### 2. Security Isolation

Use schemas to implement fine-grained security:

```sql
-- Create dedicated user for scheduler
CREATE USER [SchedulerUser] FOR LOGIN [SchedulerLogin];

-- Grant access only to scheduler schema
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::scheduler TO [SchedulerUser];
GRANT EXECUTE ON SCHEMA::scheduler TO [SchedulerUser];

-- Deny access to other schemas
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::messaging TO [SchedulerUser];
```

### 3. Multi-Tenant Scenarios

Use schemas to isolate tenant data:

```csharp
// Configuration for Tenant A
services.AddSqlScheduler(new SqlSchedulerOptions 
{ 
    ConnectionString = tenantAConnectionString,
    SchemaName = "tenant_a" 
});

// Configuration for Tenant B
services.AddSqlScheduler(new SqlSchedulerOptions 
{ 
    ConnectionString = tenantBConnectionString,
    SchemaName = "tenant_b" 
});
```

### 4. Environment-Specific Schemas

```csharp
var schemaPrefix = builder.Environment.IsDevelopment() ? "dev" : "prod";

services.AddSqlScheduler(new SqlSchedulerOptions 
{ 
    SchemaName = $"{schemaPrefix}_scheduler" 
});
```

## Database Permissions

### Minimum Required Permissions

For automatic schema deployment:

```sql
-- Create user
CREATE USER [PlatformUser] FOR LOGIN [PlatformLogin];

-- For automatic schema creation
GRANT CREATE SCHEMA TO [PlatformUser];

-- For creating objects within the schema
GRANT ALTER ON SCHEMA::scheduler TO [PlatformUser];
GRANT ALTER ON SCHEMA::messaging TO [PlatformUser];

-- For runtime operations
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::scheduler TO [PlatformUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::messaging TO [PlatformUser];
GRANT EXECUTE ON SCHEMA::scheduler TO [PlatformUser];
GRANT EXECUTE ON SCHEMA::messaging TO [PlatformUser];

-- For user-defined types
GRANT EXECUTE ON TYPE::scheduler.GuidIdList TO [PlatformUser];
GRANT EXECUTE ON TYPE::messaging.GuidIdList TO [PlatformUser];
GRANT EXECUTE ON TYPE::messaging.StringIdList TO [PlatformUser];
```

### Production Permissions (Manual Schema Management)

```sql
-- Create user without schema creation permissions
CREATE USER [PlatformUser] FOR LOGIN [PlatformLogin];

-- Runtime-only permissions (no ALTER)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::scheduler TO [PlatformUser];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::messaging TO [PlatformUser];
GRANT EXECUTE ON SCHEMA::scheduler TO [PlatformUser];
GRANT EXECUTE ON SCHEMA::messaging TO [PlatformUser];

-- For user-defined types
GRANT EXECUTE ON TYPE::scheduler.GuidIdList TO [PlatformUser];
GRANT EXECUTE ON TYPE::messaging.GuidIdList TO [PlatformUser];
GRANT EXECUTE ON TYPE::messaging.StringIdList TO [PlatformUser];
```

## Troubleshooting

### Schema Does Not Exist

**Error**: `Invalid object name 'custom_schema.TableName'`

**Solution**: Ensure `EnableSchemaDeployment = true` or create the schema manually:

```sql
CREATE SCHEMA custom_schema;
```

### Permission Denied

**Error**: `Cannot find the object "custom_schema.StoredProcedure" because it does not exist or you do not have permissions.`

**Solution**: Grant EXECUTE permissions on the schema:

```sql
GRANT EXECUTE ON SCHEMA::custom_schema TO [PlatformUser];
```

### Type Does Not Exist

**Error**: `Cannot find data type 'custom_schema.GuidIdList'`

**Solution**: User-defined types must exist in the same schema as the stored procedures. Ensure schema deployment completed successfully.

### Objects Created in Wrong Schema

**Issue**: Objects are being created in `dbo` instead of the configured schema.

**Solution**: Verify the configuration is being applied correctly:

```csharp
// Check the configuration
var options = services.BuildServiceProvider()
    .GetRequiredService<IOptions<SqlSchedulerOptions>>().Value;
Console.WriteLine($"Schema: {options.SchemaName}");

// Ensure EnableSchemaDeployment is true
Console.WriteLine($"Auto-deploy: {options.EnableSchemaDeployment}");
```

## Migration from Default Schema

If you need to migrate existing data from the `dbo` schema to a custom schema:

```sql
-- 1. Create the new schema
CREATE SCHEMA scheduler;
GO

-- 2. Move tables to new schema
ALTER SCHEMA scheduler TRANSFER dbo.Jobs;
ALTER SCHEMA scheduler TRANSFER dbo.JobRuns;
ALTER SCHEMA scheduler TRANSFER dbo.Timers;
ALTER SCHEMA scheduler TRANSFER dbo.SchedulerState;
GO

-- 3. Recreate stored procedures in new schema
-- (The platform will do this automatically on next deployment)

-- 4. Drop old procedures from dbo
DROP PROCEDURE IF EXISTS dbo.Timers_Claim;
DROP PROCEDURE IF EXISTS dbo.Timers_Ack;
-- ... etc

-- 5. Move user-defined types
-- Note: This requires recreating dependent objects
DROP TYPE IF EXISTS dbo.GuidIdList;
CREATE TYPE scheduler.GuidIdList AS TABLE (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
GO
```

## Testing with Custom Schemas

When writing integration tests with custom schemas:

```csharp
public class CustomSchemaTests : SqlServerTestBase
{
    private const string CustomSchema = "test_platform";

    [Fact]
    public async Task Scheduler_Works_WithCustomSchema()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSqlScheduler(new SqlSchedulerOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = CustomSchema,
            EnableSchemaDeployment = true,
            EnableBackgroundWorkers = false
        });
        
        var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<ISchedulerClient>();

        // Act
        var timerId = await scheduler.ScheduleTimerAsync(
            "test-topic",
            "test-payload",
            DateTimeOffset.UtcNow.AddSeconds(1));

        // Assert
        Assert.NotEqual(Guid.Empty, timerId);
    }
}
```

## Additional Resources

- [Main README](../README.md) - Platform overview and quick start
- [Work Queue Pattern Documentation](work-queue-pattern.md) - Details on work queue implementation
- [Integration Tests](../tests/Bravellian.Platform.Tests/CustomSchemaIntegrationTests.cs) - Example tests with custom schemas
