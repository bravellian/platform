# Manual Schema Export Test

This test (`ManualSchemaExportTests.cs`) provides a utility to deploy all platform schemas to a SQL Server container and extract them back to update the SQL Server project.

## Purpose

The test is designed to help maintain the SQL Server Database project (`Bravellian.Platform.Database.sqlproj`) by:
1. Deploying all platform schemas to a fresh SQL Server container
2. Extracting the deployed schema using SqlPackage
3. Generating SQL scripts that can be used to update the SQL project

## Prerequisites

- Docker must be running (for Testcontainers to spin up SQL Server)
- .NET 9.0 SDK
- SqlPackage tool (automatically installed as a local dotnet tool)

## How to Run

### Option 1: Run the test explicitly

The test is currently enabled by default (no `Skip` parameter). To run it:

1. Run the test:
   ```bash
   dotnet test --filter "FullyQualifiedName~ManualSchemaExportTests.DeploySchemaAndExportToSqlProject"
   ```

If you want to prevent the test from running automatically, you can add a `Skip` parameter to the `[Fact]` attribute in `ManualSchemaExportTests.cs`:
```csharp
[Fact(Skip = "Manual test only - run explicitly when you want to update the SQL Server project")]
```

### Option 2: Run using test explorer

1. In Visual Studio, open Test Explorer
2. Find `ManualSchemaExportTests.DeploySchemaAndExportToSqlProject`
3. Right-click and select "Run"

## What the Test Does

1. **Spins up a SQL Server container** using Testcontainers
2. **Deploys all platform schemas** including:
   - Outbox and Outbox work queue
   - Inbox and Inbox work queue
   - Scheduler (Jobs, JobRuns, Timers)
   - Lease and DistributedLock
   - Fanout (Policy and Cursor)
   - Semaphore
   - Metrics (infra schema)
3. **Extracts the schema** to a `.dacpac` file
4. **Generates SQL scripts** from the deployed database

## Output

After running the test, you'll find:
- **DACPAC file**: `src/Bravellian.Platform.Database/Bravellian.Platform.Database.dacpac`
- **SQL script**: `src/Bravellian.Platform.Database/DeployedSchema.sql`

## Next Steps

After running the test:

1. **Review the generated files** to ensure they contain the expected schema
2. **Use SQL Server Data Tools (SSDT)** in Visual Studio to:
   - Import the DACPAC or SQL script into the SQL project
   - Or manually organize the schema objects from the script into the project structure
3. **Commit the changes** to the SQL project if they are correct

## Using SSDT to Update the SQL Project

For a more integrated experience, you can use Visual Studio with SQL Server Data Tools:

1. Open the solution in Visual Studio
2. Right-click the SQL Server Database project
3. Select "Schema Compare"
4. Set the source to your deployed database (or the generated DACPAC)
5. Set the target to the project
6. Review and apply the changes

## Troubleshooting

### Docker not running
If you get an error about Docker, make sure Docker Desktop is running.

### SqlPackage errors
If SqlPackage fails, check that the tool is installed:
```bash
dotnet tool restore
```

### Schema differences
If the extracted schema differs significantly from what you expect, verify that all schema deployment methods in `DatabaseSchemaManager.cs` are being called in the test.

## Notes

- This is a **manual test** that currently runs by default
- Add a `Skip` parameter to the `[Fact]` attribute to prevent it from running in CI/CD pipelines
- The test is safe to run multiple times - it creates a fresh container each time
- The container is automatically cleaned up after the test completes
- This test does not modify your actual databases - it only uses a temporary Docker container
