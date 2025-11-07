# Outbox Router Implementation Summary

## Problem Statement
The existing outbox system supported reading from multiple databases using selection strategies, but lacked an API for **writing** messages to specific outbox databases based on a routing key. This was critical for multi-tenant scenarios where messages need to be created in the correct tenant's database.

## Solution Overview
Implemented `IOutboxRouter` interface and supporting infrastructure to enable routing write operations to the appropriate outbox database based on a configurable key (string or GUID).

## Changes Made

### New Interfaces and Classes

1. **IOutboxRouter** (`src/Bravellian.Platform/Outbox/IOutboxRouter.cs`)
   - Provides `GetOutbox(string key)` and `GetOutbox(Guid key)` methods
   - Routes write operations to the correct outbox database

2. **OutboxRouter** (`src/Bravellian.Platform/Outbox/OutboxRouter.cs`)
   - Default implementation of IOutboxRouter
   - Delegates to IOutboxStoreProvider for actual outbox lookup
   - Simple, lightweight implementation

### Enhanced Existing Interfaces

3. **IOutboxStoreProvider** (`src/Bravellian.Platform/Outbox/IOutboxStoreProvider.cs`)
   - Added `GetStoreByKey(string key)` - returns IOutboxStore by key
   - Added `GetOutboxByKey(string key)` - returns IOutbox by key

4. **ConfiguredOutboxStoreProvider** (`src/Bravellian.Platform/Outbox/ConfiguredOutboxStoreProvider.cs`)
   - Updated to create and cache IOutbox instances alongside IOutboxStore instances
   - Implements new interface methods

5. **DynamicOutboxStoreProvider** (`src/Bravellian.Platform/Outbox/DynamicOutboxStoreProvider.cs`)
   - Updated to create and cache IOutbox instances alongside IOutboxStore instances
   - Implements new interface methods
   - Outbox instances are updated when database configuration changes

6. **Service Registration** (`src/Bravellian.Platform/Scheduler/SchedulerServiceCollectionExtensions.cs`)
   - Updated `AddMultiSqlOutbox` methods to register IOutboxRouter
   - Updated `AddDynamicMultiSqlOutbox` to register IOutboxRouter

### Tests

7. **OutboxRouterTests** (`tests/Bravellian.Platform.Tests/OutboxRouterTests.cs`)
   - 9 comprehensive unit tests covering:
     - String and GUID key routing
     - Error handling (null, empty, non-existent keys)
     - Dynamic provider integration
     - Instance caching

8. **OutboxRouterIntegrationTests** (`tests/Bravellian.Platform.Tests/OutboxRouterIntegrationTests.cs`)
   - 3 integration tests demonstrating:
     - Multi-tenant scenarios
     - Dynamic discovery scenarios
     - Typical application usage patterns

9. **Test Mocks Updated**
   - MultiOutboxDispatcherTests.TestOutboxStoreProvider updated to implement new interface methods

### Documentation

10. **Usage Guide** (`docs/OutboxRouter.md`)
    - Comprehensive guide with examples for:
      - Single database setup
      - Multi-database with static configuration
      - Multi-database with dynamic discovery
      - Routing key types (string and GUID)
      - Error handling
      - Architecture notes

## Architecture Principles

### Separation of Concerns
- **Read Operations**: Use `IOutboxStoreProvider` with `IOutboxSelectionStrategy` to poll multiple databases
- **Write Operations**: Use `IOutboxRouter` with a routing key to write to the correct database

### Caching Strategy
- Outbox instances are created once and cached by the providers
- Repeated calls to `GetOutbox(key)` return the same instance
- Dynamic providers update cache when database configuration changes

### Key-Based Routing
- Providers use database identifiers as keys (e.g., "Customer1", "Tenant123")
- For ConfiguredOutboxStoreProvider: key is extracted from connection string (database name) or schema.table
- For DynamicOutboxStoreProvider: key is the `Identifier` from `OutboxDatabaseConfig`

## Usage Examples

### Single Database
```csharp
services.AddSqlOutbox(connectionString);
// Inject IOutbox directly
```

### Multi-Database (Static)
```csharp
services.AddMultiSqlOutbox(new[] { tenant1Options, tenant2Options });
// Inject IOutboxRouter
var outbox = router.GetOutbox("Tenant1");
await outbox.EnqueueAsync("topic", "payload", "correlationId");
```

### Multi-Database (Dynamic)
```csharp
services.AddSingleton<IOutboxDatabaseDiscovery, MyDiscovery>();
services.AddDynamicMultiSqlOutbox();
// Inject IOutboxRouter
var outbox = router.GetOutbox(tenantId);
await outbox.EnqueueAsync("topic", "payload", "correlationId");
```

## Testing Results
- All 71 outbox-related tests passing
- New tests: 12 (9 unit + 3 integration)
- No regressions in existing tests

## Benefits

1. **Solves the Original Problem**: Provides API for creating messages in multi-database scenarios
2. **Minimal Changes**: Leverages existing infrastructure (IOutboxStoreProvider)
3. **Backward Compatible**: Existing single-database code unchanged
4. **Type-Safe**: Supports both string and GUID routing keys
5. **Well-Tested**: Comprehensive unit and integration tests
6. **Documented**: Clear usage guide with examples

## Future Enhancements (Not Implemented)

1. Custom routing strategies (e.g., hash-based routing)
2. Default outbox fallback when key not found
3. Metrics/telemetry for routing decisions
4. Connection pooling optimization for frequently-accessed outboxes
