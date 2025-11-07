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

using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// Tests to ensure that the database schema used in tests is consistent with production schema.
/// This prevents issues where test schemas diverge from production schemas.
/// </summary>
public class DatabaseSchemaConsistencyTests : SqlServerTestBase
{
    public DatabaseSchemaConsistencyTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Apply work queue migrations to add Status, LockedUntil, OwnerToken columns
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(this.ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task DatabaseSchema_AllRequiredTablesExist()
    {
        // Arrange
        var expectedTables = new[]
        {
            "Outbox",
            "OutboxState",
            "Inbox",
            "Jobs",
            "JobRuns",
            "Timers",
            "SchedulerState",
        };

        // Act & Assert
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var tableName in expectedTables)
        {
            var exists = await this.TableExistsAsync(connection, "dbo", tableName);
            exists.ShouldBeTrue($"Table dbo.{tableName} should exist");
        }
    }

    [Fact]
    public async Task OutboxTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await this.GetTableColumnsAsync("dbo", "Outbox");

        // Assert - Check essential columns exist with correct types
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["Payload"] = "nvarchar",
            ["Topic"] = "nvarchar",
            ["CreatedAt"] = "datetimeoffset",
            ["IsProcessed"] = "bit",
            ["ProcessedAt"] = "datetimeoffset",
            ["ProcessedBy"] = "nvarchar",
            ["RetryCount"] = "int",
            ["LastError"] = "nvarchar",
            ["NextAttemptAt"] = "datetimeoffset",
            ["MessageId"] = "uniqueidentifier",
            ["CorrelationId"] = "nvarchar",

            // Work queue columns added by migration
            ["Status"] = "tinyint",
            ["LockedUntil"] = "datetime2",
            ["OwnerToken"] = "uniqueidentifier",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Outbox table");
            columns[columnName].ShouldStartWith(expectedType);
        }
    }

    [Fact]
    public async Task JobsTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await this.GetTableColumnsAsync("dbo", "Jobs");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["JobName"] = "nvarchar",
            ["CronSchedule"] = "nvarchar",
            ["Topic"] = "nvarchar",
            ["Payload"] = "nvarchar",
            ["IsEnabled"] = "bit",
            ["NextDueTime"] = "datetimeoffset",
            ["LastRunTime"] = "datetimeoffset",
            ["LastRunStatus"] = "nvarchar",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Jobs table");
            columns[columnName].ShouldStartWith(expectedType);
        }
    }

    [Fact]
    public async Task TimersTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await this.GetTableColumnsAsync("dbo", "Timers");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["DueTime"] = "datetimeoffset",
            ["Payload"] = "nvarchar",
            ["Topic"] = "nvarchar",
            ["CorrelationId"] = "nvarchar",
            ["Status"] = "nvarchar",
            ["ClaimedBy"] = "nvarchar",
            ["ClaimedAt"] = "datetimeoffset",
            ["RetryCount"] = "int",
            ["CreatedAt"] = "datetimeoffset",
            ["ProcessedAt"] = "datetimeoffset",
            ["LastError"] = "nvarchar",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Timers table");
            columns[columnName].ShouldStartWith(expectedType);
        }
    }

    [Fact]
    public async Task JobRunsTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await this.GetTableColumnsAsync("dbo", "JobRuns");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["JobId"] = "uniqueidentifier",
            ["ScheduledTime"] = "datetimeoffset",
            ["Status"] = "nvarchar",
            ["ClaimedBy"] = "nvarchar",
            ["ClaimedAt"] = "datetimeoffset",
            ["RetryCount"] = "int",
            ["StartTime"] = "datetimeoffset",
            ["EndTime"] = "datetimeoffset",
            ["Output"] = "nvarchar",
            ["LastError"] = "nvarchar",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in JobRuns table");
            columns[columnName].ShouldStartWith(expectedType);
        }
    }

    [Fact]
    public async Task InboxTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await this.GetTableColumnsAsync("dbo", "Inbox");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["MessageId"] = "varchar",
            ["Source"] = "varchar",
            ["Hash"] = "binary",
            ["FirstSeenUtc"] = "datetime2",
            ["LastSeenUtc"] = "datetime2",
            ["ProcessedUtc"] = "datetime2",
            ["Attempts"] = "int",
            ["Status"] = "varchar",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Inbox table");
            columns[columnName].ShouldStartWith(expectedType);
        }
    }

    [Fact]
    public async Task RequiredIndexes_ExistOnAllTables()
    {
        // Act & Assert
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Check critical indexes exist
        var indexExists = await this.IndexExistsAsync(connection, "dbo", "Outbox", "IX_Outbox_GetNext");
        indexExists.ShouldBeTrue("Outbox should have IX_Outbox_GetNext index");

        indexExists = await this.IndexExistsAsync(connection, "dbo", "Jobs", "UQ_Jobs_JobName");
        indexExists.ShouldBeTrue("Jobs should have UQ_Jobs_JobName index");

        indexExists = await this.IndexExistsAsync(connection, "dbo", "Timers", "IX_Timers_GetNext");
        indexExists.ShouldBeTrue("Timers should have IX_Timers_GetNext index");

        indexExists = await this.IndexExistsAsync(connection, "dbo", "JobRuns", "IX_JobRuns_GetNext");
        indexExists.ShouldBeTrue("JobRuns should have IX_JobRuns_GetNext index");
    }

    [Fact]
    public async Task CustomSchemaNames_WorkCorrectly()
    {
        // Arrange
        var customSchema = "custom_test";
        var customConnectionString = this.ConnectionString;

        // Create the custom schema first
        await using var setupConnection = new SqlConnection(customConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync($"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{customSchema}') EXEC('CREATE SCHEMA [{customSchema}]')");

        // Act - Create schema using DatabaseSchemaManager with custom schema name
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            customConnectionString,
            customSchema,
            "CustomJobs",
            "CustomJobRuns",
            "CustomTimers");

        // Assert
        await using var connection = new SqlConnection(customConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tablesExist = await this.TableExistsAsync(connection, customSchema, "CustomJobs");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobs should exist");

        tablesExist = await this.TableExistsAsync(connection, customSchema, "CustomJobRuns");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobRuns should exist");

        tablesExist = await this.TableExistsAsync(connection, customSchema, "CustomTimers");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomTimers should exist");

        // Check indexes have correct parameterized names
        var indexExists = await this.IndexExistsAsync(connection, customSchema, "CustomJobs", "UQ_CustomJobs_JobName");
        indexExists.ShouldBeTrue("Custom Jobs table should have correctly named unique index");
    }

    [Fact]
    public async Task WorkQueueColumns_ExistAfterMigration()
    {
        // Arrange & Act - WorkQueue migration should have been applied during setup
        var columns = await this.GetTableColumnsAsync("dbo", "Outbox");

        // Assert - Work queue columns should exist
        columns.ShouldContainKey("Status", "Status column should exist after work queue migration");
        columns.ShouldContainKey("LockedUntil", "LockedUntil column should exist after work queue migration");
        columns.ShouldContainKey("OwnerToken", "OwnerToken column should exist after work queue migration");

        // Check that the type dbo.GuidIdList exists
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var typeExists = await connection.QuerySingleOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM sys.types WHERE name = 'GuidIdList' AND schema_id = SCHEMA_ID('dbo')");

        typeExists.ShouldBeGreaterThan(0, "Work queue type dbo.GuidIdList should exist");
    }

    /// <summary>
    /// Helper method to check if a table exists in a specific schema.
    /// </summary>
    private async Task<bool> TableExistsAsync(SqlConnection connection, string schemaName, string tableName)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

        var count = await connection.QuerySingleAsync<int>(sql, new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);
        return count > 0;
    }

    /// <summary>
    /// Helper method to get table columns and their data types.
    /// </summary>
    private async Task<Dictionary<string, string>> GetTableColumnsAsync(string schemaName, string tableName)
    {
        var connection = new SqlConnection(this.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

            var columns = await connection.QueryAsync<(string ColumnName, string DataType)>(
                sql, new { SchemaName = schemaName, TableName = tableName });

            return columns.ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Helper method to check if an index exists.
    /// </summary>
    private async Task<bool> IndexExistsAsync(SqlConnection connection, string schemaName, string tableName, string indexName)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @SchemaName 
              AND t.name = @TableName 
              AND i.name = @IndexName";

        var count = await connection.QuerySingleAsync<int>(sql, new { SchemaName = schemaName, TableName = tableName, IndexName = indexName }).ConfigureAwait(false);
        return count > 0;
    }
}
