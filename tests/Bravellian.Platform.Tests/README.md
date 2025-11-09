# Test Infrastructure Guide

## Overview

The test suite uses xUnit v3 with optimizations to reduce Docker container overhead. Tests are organized into collections to enable efficient resource sharing while maintaining test isolation.

## Test Organization

### Test Categories

Tests are categorized using xUnit traits:

- **Integration Tests**: Tests requiring external dependencies (databases, Docker containers)
  - Trait: `[Trait("Category", "Integration")]`
  - Trait: `[Trait("RequiresDocker", "true")]`
  
- **Unit Tests**: Fast, isolated tests with no external dependencies
  - Trait: `[Trait("Category", "Unit")]`

### Test Collections

Database integration tests use the `SqlServerCollection` to share a single Docker SQL Server container across multiple test classes. This significantly reduces test execution time by avoiding repeated container startup/teardown.

#### SQL Server Collection

Tests in this collection share a Docker SQL Server container but get individual databases for isolation:

```csharp
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MyDatabaseTests : SqlServerTestBase
{
    public MyDatabaseTests(ITestOutputHelper output, SqlServerCollectionFixture fixture)
        : base(output, fixture)
    {
    }
    
    // Your tests...
}
```

**Key Benefits:**
- Single Docker container for all database tests (instead of 20+ separate containers)
- Each test class gets its own isolated database
- 68% reduction in test execution time (7.5min → 2.4min)
- Reduced Docker resource contention

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Only Unit Tests (Fast, No Docker)
```bash
dotnet test --filter "Category!=Integration"
```

### Run Only Integration Tests
```bash
dotnet test --filter "Category=Integration"
```

### Run Tests Without Docker Requirements
```bash
dotnet test --filter "RequiresDocker!=true"
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~SqlOutboxStoreTests"
```

## Configuration

### xUnit Configuration

The `xunit.runner.v3.json` file controls test execution:

```json
{
  "$schema": "https://xunit.net/schema/v3/xunit.runner.schema.json",
  "parallelizeAssembly": true,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4,
  "methodDisplay": "method",
  "methodDisplayOptions": "all",
  "stopOnFail": false,
  "longRunningTestSeconds": 60,
  "diagnosticMessages": false
}
```

- **parallelizeAssembly**: Run tests from the assembly in parallel
- **parallelizeTestCollections**: Run different collections in parallel
- **maxParallelThreads**: Limit concurrent test execution to avoid resource exhaustion
- **methodDisplay**: How test names are displayed (method name only)
- **methodDisplayOptions**: Display options for test names (all available info)
- **stopOnFail**: Continue running tests even if one fails
- **longRunningTestSeconds**: Tests taking longer than this will be flagged
- **diagnosticMessages**: Whether to show diagnostic messages during test runs

## Writing Tests

### For Database Tests

Inherit from `SqlServerTestBase` and join the `SqlServerCollection`:

```csharp
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MyDatabaseTests : SqlServerTestBase
{
    public MyDatabaseTests(ITestOutputHelper output, SqlServerCollectionFixture fixture)
        : base(output, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        // Additional setup if needed
    }

    [Fact]
    public async Task MyTest()
    {
        // Use this.ConnectionString to access the database
        await using var connection = new SqlConnection(this.ConnectionString);
        // Your test logic...
    }
}
```

### For Unit Tests

Simple unit tests don't need a base class:

```csharp
[Trait("Category", "Unit")]
public class MyUnitTests
{
    [Fact]
    public void MyTest()
    {
        // Your test logic...
    }
}
```

## Best Practices

1. **Use Appropriate Base Classes**: Use `SqlServerTestBase` only when you need a database
2. **Tag Tests Correctly**: Always add appropriate `[Trait]` attributes
3. **Minimize Database Tests**: Convert to unit tests when possible
4. **Test Isolation**: Don't rely on execution order - each test should be independent
5. **Async All The Way**: Use async/await for all I/O operations
6. **Resource Cleanup**: Let xUnit handle cleanup via `IAsyncLifetime`

## Performance Tips

- Tests in the same collection run sequentially but collections run in parallel
- Unit tests are much faster than integration tests - prefer unit tests
- Each database test class gets its own database, so schema changes don't affect other classes
- The shared container approach saves ~5 minutes on a full test run

## Troubleshooting

### "Container has not been started yet" Error
This means `InitializeAsync` wasn't called. Make sure you're not calling `ConnectionString` in the constructor.

### Tests Timing Out
- Check if Docker has enough resources allocated
- Verify no other containers are causing resource contention
- Consider splitting large test classes into smaller ones

### Flaky Tests
- Ensure tests don't depend on execution order
- Check for shared state between tests
- Verify proper cleanup in `DisposeAsync`

## Architecture

### SqlServerCollectionFixture

- Creates a single MsSql Docker container
- Provides `CreateTestDatabaseAsync()` to create isolated databases
- Managed by xUnit's collection fixture lifecycle
- Container is reused across all test classes in the collection

### SqlServerTestBase

- Supports two modes:
  - **Standalone**: Creates its own container (legacy support)
  - **Shared**: Uses SqlServerCollectionFixture (recommended)
- Provides connection string and database schema setup
- Implements `IAsyncLifetime` for proper async initialization/disposal

## Migration Guide

### Converting Existing Test Class to Use Shared Container

**Before:**
```csharp
public class MyTests : SqlServerTestBase
{
    public MyTests(ITestOutputHelper output)
        : base(output)
    {
    }
}
```

**After:**
```csharp
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MyTests : SqlServerTestBase
{
    public MyTests(ITestOutputHelper output, SqlServerCollectionFixture fixture)
        : base(output, fixture)
    {
    }
}
```

Changes needed:
1. Add `[Collection(SqlServerCollection.Name)]` attribute
2. Add trait attributes for categorization
3. Add `SqlServerCollectionFixture fixture` parameter to constructor
4. Pass `fixture` to base constructor
