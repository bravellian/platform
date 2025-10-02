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
        await this.msSqlContainer.StartAsync();
        this.connectionString = this.msSqlContainer.GetConnectionString();
        await this.SetupDatabaseSchema();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await this.msSqlContainer.DisposeAsync();
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components.
    /// Uses the production DatabaseSchemaManager to ensure consistency.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        this.TestOutputHelper.WriteLine($"Starting SQL Server container...");
        this.TestOutputHelper.WriteLine($"SQL Server container started. Connection string: {this.connectionString}");

        // Use the production DatabaseSchemaManager to ensure consistency with production schema
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(this.connectionString);
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.connectionString);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.connectionString);
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(this.connectionString);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(this.connectionString);

        this.TestOutputHelper.WriteLine($"Database schema created successfully using production DatabaseSchemaManager");
    }

    /// <summary>
    /// Gets table column information for schema validation.
    /// </summary>
    protected async Task<Dictionary<string, string>> GetTableColumnsAsync(string schemaName, string tableName)
    {
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

        var columns = new Dictionary<string, string>();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@TableName", tableName);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var columnName = reader.GetString(0); // COLUMN_NAME
            var dataType = reader.GetString(1);   // DATA_TYPE
            columns[columnName] = dataType;
        }

        return columns;
    }

    /// <summary>
    /// Checks if a table exists in the database.
    /// </summary>
    protected async Task<bool> TableExistsAsync(SqlConnection connection, string schemaName, string tableName)
    {
        const string sql = @"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@TableName", tableName);

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }
}