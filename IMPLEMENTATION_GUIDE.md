# Platform Registration Unification - Implementation Guide

## Overview

This document describes the implementation of unified platform registration as specified in the issue. The goal is to consolidate platform registration into exactly 5 methods, introduce a unified database discovery interface, and formalize three environment styles.

## Completed Work

### Phase 1: Core Abstractions ✅

1. **`PlatformEnvironmentStyle` enum** - Defines the two supported environment styles:
   - `MultiDatabaseNoControl` - Features run across multiple databases with round-robin scheduling (use with a single database for simple scenarios)
   - `MultiDatabaseWithControl` - Features run across multiple databases with control plane coordination

2. **`IPlatformDatabaseDiscovery` interface** - Unified discovery abstraction:
   ```csharp
   public interface IPlatformDatabaseDiscovery
   {
       Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default);
   }
   ```

3. **`PlatformDatabase` class** - Represents a single application database:
   ```csharp
   public sealed class PlatformDatabase
   {
       public required string Name { get; init; }
       public required string ConnectionString { get; init; }
       public string SchemaName { get; init; } = "dbo";
   }
   ```

4. **`PlatformConfiguration` class** - Internal configuration state tracking:
   ```csharp
   internal sealed class PlatformConfiguration
   {
       public required PlatformEnvironmentStyle EnvironmentStyle { get; init; }
       public required bool UsesDiscovery { get; init; }
       public string? ControlPlaneConnectionString { get; init; }
       public bool EnableSchemaDeployment { get; init; }
   }
   ```

5. **`ListBasedDatabaseDiscovery`** - Implementation for static lists:
   - Validates unique database names
   - Validates non-empty lists
   - Returns static list synchronously

6. **`PlatformLifecycleService`** - Startup validation service:
   - Validates multi-database configuration (list or discovery)
   - Validates control plane connectivity
   - Logs environment style and database count
   - Fails fast with clear error messages

### Phase 2: Registration Methods ✅

Implemented all 4 registration methods in `PlatformServiceCollectionExtensions.cs`:

1. **`AddPlatformMultiDatabaseWithList`** - Multi-DB with explicit list (use with single item for single database scenarios)
   ```csharp
   var databases = new[]
   {
       new PlatformDatabase { Name = "db1", ConnectionString = "..." },
       new PlatformDatabase { Name = "db2", ConnectionString = "..." }
   };
   services.AddPlatformMultiDatabaseWithList(databases);
   ```

2. **`AddPlatformMultiDatabaseWithDiscovery`** - Multi-DB with dynamic discovery
   ```csharp
   services.AddSingleton<IPlatformDatabaseDiscovery, MyDiscoveryImpl>();
   services.AddPlatformMultiDatabaseWithDiscovery();
   ```

3. **`AddPlatformMultiDatabaseWithControlPlaneAndList`** - Multi-DB + control plane with list
   ```csharp
   services.AddPlatformMultiDatabaseWithControlPlaneAndList(
       databases,
       controlPlaneConnectionString: "...");
   ```

4. **`AddPlatformMultiDatabaseWithControlPlaneAndDiscovery`** - Multi-DB + control plane with discovery
   ```csharp
   services.AddSingleton<IPlatformDatabaseDiscovery, MyDiscoveryImpl>();
   services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
       controlPlaneConnectionString: "...");
   ```

**Key Features:**
- Mutual exclusivity enforced - only one can be called
- Clear error messages if multiple registrations attempted
- Validates all inputs (non-null, non-empty, etc.)
- Registers time abstractions (`TimeProvider`, `IMonotonicClock`)
- Registers schema deployment if enabled
- Registers lifecycle service for startup validation

## Remaining Work

### Phase 3: Feature Integration (TODO)

Each existing feature needs to be adapted to use the unified discovery. The pattern is:

1. Create a provider adapter that implements the existing provider interface using `IPlatformDatabaseDiscovery`
2. Create feature extension methods that:
   - Check that platform is registered
   - Get `PlatformConfiguration` to determine environment style
   - Register appropriate services based on single vs multi-database

#### Example Pattern (for each feature)

```csharp
// 1. Create provider adapter
internal class PlatformOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private Dictionary<string, IOutboxStore> stores = new();
    
    public async Task InitializeAsync()
    {
        var databases = await discovery.DiscoverDatabasesAsync();
        stores = databases.ToDictionary(
            db => db.Name,
            db => (IOutboxStore)new SqlOutboxStore(db.ConnectionString, db.SchemaName, ...));
    }
    
    public IReadOnlyList<IOutboxStore> GetAllStores() => stores.Values.ToList();
    public string GetStoreIdentifier(IOutboxStore store) => /* implementation */;
    public IOutboxStore GetStoreByKey(string key) => stores[key];
    // etc.
}

// 2. Create feature extension
public static IServiceCollection AddOutboxFeature(this IServiceCollection services)
{
    EnsurePlatformIsRegistered(services);
    var config = GetPlatformConfiguration(services);
    
    // All registrations now use multi-DB services
    // For single database scenarios, use discovery that returns one database
    services.AddSingleton<IOutboxStoreProvider, PlatformOutboxStoreProvider>();
    // etc.
    
    return services;
}
```

Features to update:
- [ ] Outbox
- [ ] Inbox
- [ ] Scheduler (Timers + Jobs)
- [ ] Fanout
- [ ] Leases

### Phase 4: Remove Old Methods (TODO)

Once feature integration is complete, remove:

1. **Old registration methods from `SchedulerServiceCollectionExtensions.cs`:**
   - `AddSqlScheduler(IConfiguration)`
   - `AddSqlScheduler(SqlSchedulerOptions)`
   - `AddSqlOutbox(SqlOutboxOptions)`
   - `AddSqlInbox(SqlInboxOptions)`
   - `AddMultiSqlOutbox(...)`
   - `AddDynamicMultiSqlOutbox(...)`
   - etc.

2. **Old discovery interfaces:**
   - `IOutboxDatabaseDiscovery`
   - `IInboxDatabaseDiscovery`
   - `ISchedulerDatabaseDiscovery`
   - `ILeaseDatabaseDiscovery`
   - `IFanoutDatabaseDiscovery`

3. **Old provider implementations:**
   - `DynamicOutboxStoreProvider`
   - `ConfiguredOutboxStoreProvider`
   - `DynamicInboxWorkStoreProvider`
   - `ConfiguredInboxWorkStoreProvider`
   - etc.

### Phase 5: Testing (TODO)

Create comprehensive tests:

1. **Unit tests for registration:**
   ```csharp
   [Fact]
   public void AddPlatform_CalledTwice_ThrowsException()
   {
       var services = new ServiceCollection();
       services.AddPlatformMultiDatabaseWithList(new[] { 
           new PlatformDatabase { Name = "db1", ConnectionString = "connStr" }
       });
       
       var ex = Assert.Throws<InvalidOperationException>(
           () => services.AddPlatformMultiDatabaseWithList(new[] { 
               new PlatformDatabase { Name = "db2", ConnectionString = "connStr2" }
           }));
       
       Assert.Contains("already been called", ex.Message);
   }
   ```

2. **Integration tests for each environment style:**
   - Multi-database with list: round-robin observed
   - Multi-database with discovery: dynamic discovery works
   - Control plane variants: validation passes

3. **Validation tests:**
   - Empty list rejected
   - Duplicate names rejected
   - Discovery returns no DBs: clear error
   - Control plane unreachable: clear error

### Phase 6: Documentation (TODO)

1. **New documentation:**
   - Environment Styles & Registration guide
   - Database Discovery guide
   - Migration guide from old to new methods

2. **Update existing docs:**
   - Quickstart examples
   - README.md
   - API references

## Migration Guide (Draft)

### From Old to New

**Single Database Scenarios:**
```csharp
// NEW - Use multi-database with a single item list
services.AddPlatformMultiDatabaseWithList(new[]
{
    new PlatformDatabase 
    { 
        Name = "default", 
        ConnectionString = connStr,
        SchemaName = "dbo"
    }
});
```

**Multi-Database (List):**
```csharp
// OLD
services.AddMultiSqlOutbox(new[]
{
    new SqlOutboxOptions { ConnectionString = conn1 },
    new SqlOutboxOptions { ConnectionString = conn2 }
});

// NEW
services.AddPlatformMultiDatabaseWithList(new[]
{
    new PlatformDatabase { Name = "db1", ConnectionString = conn1 },
    new PlatformDatabase { Name = "db2", ConnectionString = conn2 }
}).AddOutboxFeature();
```

**Multi-Database (Discovery):**
```csharp
// OLD
services.AddSingleton<IOutboxDatabaseDiscovery, MyDiscovery>();
services.AddDynamicMultiSqlOutbox();

// NEW
services.AddSingleton<IPlatformDatabaseDiscovery, MyPlatformDiscovery>();
services.AddPlatformMultiDatabaseWithDiscovery()
    .AddOutboxFeature();
```

## Design Rationale

### Why 5 Methods?

The 5 methods map directly to the 3 environment styles × 2 database source modes:
1. Single database (implicit list of 1)
2. Multi-database without control plane (list)
3. Multi-database without control plane (discovery)
4. Multi-database with control plane (list)
5. Multi-database with control plane (discovery)

This makes the environment style and database source explicit at registration.

### Why Unified Discovery?

Previously, each feature had its own discovery interface:
- `IOutboxDatabaseDiscovery`
- `IInboxDatabaseDiscovery`
- etc.

This caused:
- Code duplication
- Inconsistent discovery behavior
- Difficult to maintain

The unified `IPlatformDatabaseDiscovery` solves this by:
- Single abstraction for all features
- Consistent behavior across platform
- Easier to implement and test
- Clear contract (read-only, idempotent)

### Why Control Plane as Registration Concern?

Control plane is a deployment-level decision, not a runtime concern. Making it explicit at registration:
- Validates connectivity at startup
- Makes architecture clear from code
- Enables future control-plane features without API changes
- Separates concerns (control vs application databases)

## Implementation Status

| Component | Status | Notes |
|-----------|--------|-------|
| Core abstractions | ✅ Complete | Enum, interfaces, classes |
| Registration methods | ✅ Complete | All 5 methods implemented |
| Lifecycle validation | ✅ Complete | Startup checks working |
| Outbox integration | ❌ TODO | Need provider adapter |
| Inbox integration | ❌ TODO | Need provider adapter |
| Scheduler integration | ❌ TODO | Need provider adapter |
| Fanout integration | ❌ TODO | Need provider adapter |
| Leases integration | ❌ TODO | Need provider adapter |
| Remove old methods | ❌ TODO | After integration complete |
| Tests | ❌ TODO | After integration complete |
| Documentation | ❌ TODO | After tests pass |

## Next Steps

1. Implement provider adapters for one feature (e.g., Outbox) as reference
2. Create feature extension methods
3. Test single-DB and multi-DB scenarios
4. Repeat for other features
5. Remove old registrations
6. Update all tests
7. Write documentation
8. Create migration guide
