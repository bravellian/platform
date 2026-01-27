// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


using Testcontainers.MsSql;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Manual test for deploying schema to a SQL Server container and extracting it back to the SQL project.
/// This is not an automated test - it's a utility to update the SQL Server project from deployed schema.
///
/// To run this test:
/// 1. Execute: dotnet test --filter "FullyQualifiedName~ManualSchemaExportTests.DeploySchemaAndExportToSqlProject"
/// 2. The test will:
///    - Spin up a SQL Server container
///    - Deploy all platform schemas
///    - Extract the schema to a .dacpac file
///    - Update the SQL Server project with the extracted schema
/// </summary>
public class ManualSchemaExportTests : IAsyncLifetime
{
    private MsSqlContainer? msSqlContainer;
    private string? connectionString;

    public async ValueTask InitializeAsync()
    {
        // Start SQL Server container
        msSqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();

        await msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        connectionString = msSqlContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (msSqlContainer != null)
        {
            await msSqlContainer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// This manual test deploys all platform schemas to a fresh SQL Server container
    /// and then uses SqlPackage to extract the schema and update the SQL Server project.
    ///
    /// This test creates two separate databases and two separate dacpac files:
    /// 1. Control Plane database - contains Semaphore and Central Metrics schemas
    /// 2. Multi-Database schema - contains Outbox, Inbox, Scheduler, Lease, Fanout, Metrics, and DistributedLock schemas
    ///
    /// Note: This test is skipped by default to prevent it from running in CI.
    /// To run it, remove the Skip parameter or run it explicitly using the test filter.
    /// </summary>
    /// <summary>
    /// When DeploySchemaAndExportToSqlProject runs against a fresh SQL Server container, then schemas are deployed and DACPACs are exported to the SQL project.
    /// </summary>
    /// <intent>
    /// Provide a manual workflow for updating the SQL Server project from deployed schema.
    /// </intent>
    /// <scenario>
    /// Given an initialized SQL Server container and a valid connection string for schema deployment.
    /// </scenario>
    /// <behavior>
    /// Then control plane and multi-database schemas are deployed and the extraction steps complete without error.
    /// </behavior>
    [Fact(Skip = "Manual test only - run explicitly when you want to update the SQL Server project")]
    public async Task DeploySchemaAndExportToSqlProject()
    {
        // Ensure connection string is set
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string is not initialized. Ensure InitializeAsync was called.");
        }

        var projectRoot = GetProjectRoot();
        var sqlProjectPath = Path.Combine(projectRoot, "src", "Bravellian.Platform.Database");

        // Ensure the SQL project directory exists
        Directory.CreateDirectory(sqlProjectPath);

        // Create and deploy Control Plane database
        Console.WriteLine("=== Creating Control Plane Database ===");
        string controlPlaneConnectionString = await CreateAndDeployControlPlaneDatabase(connectionString);

        // Create and deploy Multi-Database schema
        Console.WriteLine("\n=== Creating Multi-Database Schema ===");
        string multiDatabaseConnectionString = await CreateAndDeployMultiDatabase(connectionString);

        // Extract Control Plane dacpac
        Console.WriteLine("\n=== Extracting Control Plane DACPAC ===");
        var controlPlaneDacpacPath = Path.Combine(sqlProjectPath, "Bravellian.Platform.ControlPlane.dacpac");
        Console.WriteLine($"Extracting Control Plane schema to: {controlPlaneDacpacPath}");
        await ExtractDacpac(controlPlaneConnectionString, controlPlaneDacpacPath);
        Console.WriteLine($"Control Plane DACPAC file created at: {controlPlaneDacpacPath}");

        // Extract Multi-Database dacpac
        Console.WriteLine("\n=== Extracting Multi-Database DACPAC ===");
        var multiDatabaseDacpacPath = Path.Combine(sqlProjectPath, "Bravellian.Platform.MultiDatabase.dacpac");
        Console.WriteLine($"Extracting Multi-Database schema to: {multiDatabaseDacpacPath}");
        await ExtractDacpac(multiDatabaseConnectionString, multiDatabaseDacpacPath);
        Console.WriteLine($"Multi-Database DACPAC file created at: {multiDatabaseDacpacPath}");

        // Now update the SQL project from the databases
        Console.WriteLine("\n=== Updating SQL Server Project ===");
        Console.WriteLine("Updating SQL Server project from deployed databases...");
        await UpdateSqlProjectFromDatabase(controlPlaneConnectionString, sqlProjectPath);
        await UpdateSqlProjectFromDatabase(multiDatabaseConnectionString, sqlProjectPath);

        Console.WriteLine("\n=== Summary ===");
        Console.WriteLine("SQL Server project updated successfully.");
        Console.WriteLine($"Control Plane DACPAC: {controlPlaneDacpacPath}");
        Console.WriteLine($"Multi-Database DACPAC: {multiDatabaseDacpacPath}");
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("1. Review the changes in the SQL project");
        Console.WriteLine("2. Commit the updated SQL project files if the changes are correct");
    }

    /// <summary>
    /// Creates and deploys the Control Plane database with Semaphore and Central Metrics schemas.
    /// </summary>
    private async Task<string> CreateAndDeployControlPlaneDatabase(string baseConnectionString)
    {
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder = new(baseConnectionString);
        string databaseName = "BravellianPlatform_ControlPlane";
        builder.InitialCatalog = "master";

        var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            var createDbCommand = connection.CreateCommand();
            createDbCommand.CommandText = $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}];";
            await createDbCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            await connection.CloseAsync().ConfigureAwait(false);
        }

        builder.InitialCatalog = databaseName;
        string controlPlaneConnectionString = builder.ConnectionString;

        Console.WriteLine($"Deploying Control Plane schemas to database: {databaseName}");
        Console.WriteLine($"Connection string: {controlPlaneConnectionString}");

        // Deploy Control Plane schemas
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(controlPlaneConnectionString, "infra").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureCentralMetricsSchemaAsync(controlPlaneConnectionString, "infra").ConfigureAwait(false);

        Console.WriteLine("Control Plane schema deployment completed successfully.");

        return controlPlaneConnectionString;
    }

    /// <summary>
    /// Creates and deploys the Multi-Database with all tenant/application-level schemas.
    /// </summary>
    private async Task<string> CreateAndDeployMultiDatabase(string baseConnectionString)
    {
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder = new(baseConnectionString);
        string databaseName = "BravellianPlatform_MultiDatabase";
        builder.InitialCatalog = "master";

        var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            var createDbCommand = connection.CreateCommand();
            createDbCommand.CommandText = $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}];";
            await createDbCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            await connection.CloseAsync().ConfigureAwait(false);
        }

        builder.InitialCatalog = databaseName;
        string multiDatabaseConnectionString = builder.ConnectionString;

        Console.WriteLine($"Deploying Multi-Database schemas to database: {databaseName}");
        Console.WriteLine($"Connection string: {multiDatabaseConnectionString}");

        // Deploy Multi-Database schemas
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(multiDatabaseConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(multiDatabaseConnectionString, "infra").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(multiDatabaseConnectionString, "infra", "Inbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(multiDatabaseConnectionString, "infra").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(multiDatabaseConnectionString, "infra", "Jobs", "JobRuns", "Timers").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(multiDatabaseConnectionString, "infra", "Lease").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(multiDatabaseConnectionString, "infra", "DistributedLock").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(multiDatabaseConnectionString, "infra", "FanoutPolicy", "FanoutCursor").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureMetricsSchemaAsync(multiDatabaseConnectionString, "infra").ConfigureAwait(false);

        Console.WriteLine("Multi-Database schema deployment completed successfully.");

        return multiDatabaseConnectionString;
    }

    /// <summary>
    /// Extracts the database schema to a .dacpac file using SqlPackage.
    /// </summary>
    private static async Task ExtractDacpac(string connectionString, string dacpacPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"sqlpackage /Action:Extract /SourceConnectionString:\"{connectionString}\" /TargetFile:\"{dacpacPath}\" /p:ExtractAllTableData=false /p:VerifyExtraction=true",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start SqlPackage process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SqlPackage Extract failed with exit code {process.ExitCode}\nOutput: {output}\nError: {error}");
        }

        Console.WriteLine($"SqlPackage output: {output}");
    }

    /// <summary>
    /// Updates the SQL Server project by extracting the schema directly to SQL script files.
    /// This uses SqlPackage to script out the database objects.
    /// </summary>
    private static async Task UpdateSqlProjectFromDatabase(string connectionString, string projectPath)
    {
        var infraScriptsPath = Path.Combine(projectPath, "infra");

        // Create directories if they don't exist
        Directory.CreateDirectory(infraScriptsPath);
        Directory.CreateDirectory(Path.Combine(infraScriptsPath, "Tables"));
        Directory.CreateDirectory(Path.Combine(infraScriptsPath, "Stored Procedures"));

        // Use SqlPackage to script out the database
        var scriptFilePath = Path.Combine(projectPath, "DeployedSchema.dacpac");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"sqlpackage /Action:Extract /SourceConnectionString:\"{connectionString}\" /TargetFile:\"{scriptFilePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start SqlPackage process");
        }

        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SqlPackage Script failed with exit code {process.ExitCode}\nOutput: {output}\nError: {error}");
        }

        Console.WriteLine($"SqlPackage output: {output}");
        Console.WriteLine($"Generated script file: {scriptFilePath}");
        Console.WriteLine("\nIMPORTANT: The script file has been generated.");
        Console.WriteLine("You need to manually organize it into the SQL Server project structure.");
        Console.WriteLine("Consider using SQL Server Data Tools in Visual Studio for this task.");
    }

    /// <summary>
    /// Gets the project root directory by walking up from the current directory.
    /// </summary>
    private static string GetProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null && !File.Exists(Path.Combine(directory, "Bravellian.Platform.slnx")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Could not find project root directory");
        }

        return directory;
    }
}

