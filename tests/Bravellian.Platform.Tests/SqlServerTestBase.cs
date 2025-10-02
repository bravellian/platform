namespace Bravellian.Platform.Tests;

using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

/// <summary>
/// Base test class that provides a SQL Server TestContainer for integration testing.
/// Automatically manages the container lifecycle and database schema setup.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer msSqlContainer;
    private string? connectionString;
    
    protected SqlServerTestBase(ITestOutputHelper testOutputHelper)
    {
        this.msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();
            
        this.TestOutputHelper = testOutputHelper;
    }

    protected ITestOutputHelper TestOutputHelper { get; }
    
    /// <summary>
    /// Gets the connection string for the running SQL Server container.
    /// Only available after InitializeAsync has been called.
    /// </summary>
    protected string ConnectionString => this.connectionString ?? throw new InvalidOperationException("Container has not been started yet. Make sure InitializeAsync has been called.");

    public virtual async ValueTask InitializeAsync()
    {
        try
        {
            this.TestOutputHelper.WriteLine("Starting SQL Server container...");
            await this.msSqlContainer.StartAsync();
            this.connectionString = this.msSqlContainer.GetConnectionString();
            this.TestOutputHelper.WriteLine($"SQL Server container started. Connection string: {this.connectionString}");
            
            // Wait a moment for SQL Server to fully initialize
            await Task.Delay(TimeSpan.FromSeconds(5));
            
            await this.SetupDatabaseSchema();
        }
        catch (Exception ex)
        {
            this.TestOutputHelper.WriteLine($"Failed to initialize SQL Server container: {ex}");
            throw;
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        await this.msSqlContainer.DisposeAsync();
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components using the production DatabaseSchemaManager.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        // Use the production DatabaseSchemaManager to ensure test and production schemas are identical
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(this.connectionString!);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.connectionString!);
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(this.connectionString!);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(this.connectionString!);
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.connectionString!);

        this.TestOutputHelper.WriteLine($"Database schema created successfully using production DatabaseSchemaManager");
    }

}