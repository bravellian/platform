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

namespace Bravellian.Platform.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Testcontainers.MsSql;

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
        this.msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();

        await this.msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        this.connectionString = this.msSqlContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (this.msSqlContainer != null)
        {
            await this.msSqlContainer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// This manual test deploys all platform schemas to a fresh SQL Server container
    /// and then uses SqlPackage to extract the schema and update the SQL Server project.
    /// 
    /// Note: This is marked with [Fact(Skip = "...")] to prevent it from running in CI.
    /// To run manually, comment out the Skip parameter.
    /// </summary>
    [Fact(Skip = "Manual test only - run explicitly when you want to update the SQL Server project")]
    public async Task DeploySchemaAndExportToSqlProject()
    {
        // Ensure connection string is set
        if (string.IsNullOrEmpty(this.connectionString))
        {
            throw new InvalidOperationException("Connection string is not initialized. Ensure InitializeAsync was called.");
        }

        // Arrange - Deploy all schemas to the container
        Console.WriteLine("Deploying platform schemas to SQL Server container...");
        Console.WriteLine($"Connection string: {this.connectionString}");

        // Deploy all platform schemas
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(this.connectionString, "dbo", "Outbox");
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.connectionString, "dbo");
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.connectionString, "dbo", "Inbox");
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(this.connectionString, "dbo");
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(this.connectionString, "dbo", "Jobs", "JobRuns", "Timers");
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(this.connectionString, "dbo", "Lease");
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(this.connectionString, "dbo", "DistributedLock");
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(this.connectionString, "dbo", "FanoutPolicy", "FanoutCursor");
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(this.connectionString, "dbo");
        await DatabaseSchemaManager.EnsureMetricsSchemaAsync(this.connectionString, "infra");

        Console.WriteLine("Schema deployment completed successfully.");

        // Act - Extract schema using SqlPackage
        var projectRoot = GetProjectRoot();
        var sqlProjectPath = Path.Combine(projectRoot, "src", "Bravellian.Platform.Database");
        var dacpacPath = Path.Combine(sqlProjectPath, "Bravellian.Platform.Database.dacpac");

        Console.WriteLine($"SQL Project path: {sqlProjectPath}");
        Console.WriteLine($"Extracting schema to: {dacpacPath}");

        // Extract the schema to a .dacpac file
        await ExtractDacpac(this.connectionString, dacpacPath).ConfigureAwait(false);

        Console.WriteLine("Schema extraction completed successfully.");
        Console.WriteLine($"DACPAC file created at: {dacpacPath}");

        // Now update the SQL project from the database
        Console.WriteLine("Updating SQL Server project from deployed database...");
        await UpdateSqlProjectFromDatabase(this.connectionString, sqlProjectPath).ConfigureAwait(false);

        Console.WriteLine("SQL Server project updated successfully.");
        Console.WriteLine("\nNext steps:");
        Console.WriteLine("1. Review the changes in the SQL project");
        Console.WriteLine("2. Commit the updated SQL project files if the changes are correct");
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

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await process.WaitForExitAsync().ConfigureAwait(false);

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
        var scriptsPath = Path.Combine(projectPath, "dbo");
        
        // Create directories if they don't exist
        Directory.CreateDirectory(scriptsPath);
        Directory.CreateDirectory(Path.Combine(scriptsPath, "Tables"));
        Directory.CreateDirectory(Path.Combine(scriptsPath, "Stored Procedures"));

        var infraScriptsPath = Path.Combine(projectPath, "infra");
        Directory.CreateDirectory(infraScriptsPath);
        Directory.CreateDirectory(Path.Combine(infraScriptsPath, "Tables"));
        Directory.CreateDirectory(Path.Combine(infraScriptsPath, "Stored Procedures"));

        // Use SqlPackage to script out the database
        var scriptFilePath = Path.Combine(projectPath, "DeployedSchema.sql");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"sqlpackage /Action:Script /SourceConnectionString:\"{connectionString}\" /TargetFile:\"{scriptFilePath}\" /p:ScriptDatabaseOptions=false /p:ScriptDeployStateChecks=false",
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

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await process.WaitForExitAsync().ConfigureAwait(false);

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
        while (directory != null && !File.Exists(Path.Combine(directory, "Bravellian.Platform.sln")))
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
