# Manual Schema Export Test

This test (`ManualSchemaExportTests.cs`) provides a utility to deploy all platform schemas to SQL Server containers and extract them back to update the SQL Server project.

## Purpose

The test is designed to help maintain the SQL Server Database project (`Bravellian.Platform.Database.sqlproj`) by:
1. Deploying platform schemas to two separate SQL Server databases (Control Plane and Multi-Database)
2. Extracting the deployed schemas using SqlPackage
3. Generating two separate DACPAC files that can be applied independently

## Prerequisites

- Docker must be running (for Testcontainers to spin up SQL Server)
- .NET 9.0 SDK
- SqlPackage tool (automatically installed as a local dotnet tool)

## How to Run

### Option 1: Run the test explicitly

The test is currently disabled by default (has a `Skip` parameter). To run it:

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
2. **Creates two separate databases**:
   - **Control Plane Database** (`BravellianPlatform_ControlPlane`)
   - **Multi-Database Schema** (`BravellianPlatform_MultiDatabase`)
3. **Deploys Control Plane schemas** to the Control Plane database:
   - Central Metrics (infra schema)
4. **Deploys Multi-Database schemas** to the Multi-Database:
   - Outbox and Outbox work queue
   - Inbox and Inbox work queue
   - Scheduler (Jobs, JobRuns, Timers)
   - Lease and DistributedLock
   - Fanout (Policy and Cursor)
   - Metrics (local metrics, infra schema)
5. **Extracts two separate DACPAC files**:
   - Control Plane DACPAC
   - Multi-Database DACPAC

## Output

After running the test, you'll find:
- **Control Plane DACPAC**: `src/Bravellian.Platform.Database/Bravellian.Platform.ControlPlane.dacpac`
- **Multi-Database DACPAC**: `src/Bravellian.Platform.Database/Bravellian.Platform.MultiDatabase.dacpac`

These two DACPAC files can be applied separately:
- The Control Plane DACPAC is applied to the central control plane database
- The Multi-Database DACPAC is applied to each tenant/application database

## Next Steps

After running the test:

1. **Review the generated DACPAC files** to ensure they contain the expected schemas
2. **Use the DACPAC files** to:
   - Deploy the Control Plane DACPAC to the central control plane database
   - Deploy the Multi-Database DACPAC to each tenant/application database
3. **Use SQL Server Data Tools (SSDT)** in Visual Studio to:
   - Import the DACPACs into the SQL project
   - Or use Schema Compare to sync the project with the DACPACs
4. **Commit the changes** to the SQL project if they are correct

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
