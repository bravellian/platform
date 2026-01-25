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


using Dapper;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests to ensure that the database schema used in tests is consistent with production schema.
/// This prevents issues where test schemas diverge from production schemas.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class DatabaseSchemaConsistencyTests : SqlServerTestBase
{
    public DatabaseSchemaConsistencyTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        if (SchemaVersionSnapshot.ShouldRefreshFromEnvironment())
        {
            // In snapshot refresh mode we only regenerate the schema snapshot files and
            // intentionally skip database initialization (including base.InitializeAsync
            // and schema deployment) to avoid unnecessary work and side effects.
            return;
        }

        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure schemas for all modules use the production deployment paths
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>When the schema is deployed, then all required core tables exist in the infra schema.</summary>
    /// <intent>Verify core platform tables are created in the test database.</intent>
    /// <scenario>Given schema deployment for outbox, inbox, scheduler, and fanout modules.</scenario>
    /// <behavior>Then the expected table names are present under infra.</behavior>
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
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var tableName in expectedTables)
        {
            var exists = await TableExistsAsync(connection, "infra", tableName);
            exists.ShouldBeTrue($"Table infra.{tableName} should exist");
        }
    }

    /// <summary>When inspecting the Outbox table, then required columns exist with expected data types.</summary>
    /// <intent>Ensure outbox schema matches production column definitions.</intent>
    /// <scenario>Given the Outbox table deployed in the infra schema.</scenario>
    /// <behavior>Then all essential outbox and work-queue columns are present with correct types.</behavior>
    [Fact]
    public async Task OutboxTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await GetTableColumnsAsync("infra", "Outbox");

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
            ["MessageId"] = "uniqueidentifier",
            ["CorrelationId"] = "nvarchar",
            ["DueTimeUtc"] = "datetime2",

            // Work queue columns
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

    /// <summary>When inspecting the Jobs table, then required columns exist with expected data types.</summary>
    /// <intent>Ensure jobs schema matches production column definitions.</intent>
    /// <scenario>Given the Jobs table deployed in the infra schema.</scenario>
    /// <behavior>Then all job definition columns are present with correct types.</behavior>
    [Fact]
    public async Task JobsTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await GetTableColumnsAsync("infra", "Jobs");

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

    /// <summary>When inspecting the Timers table, then required columns exist with expected data types.</summary>
    /// <intent>Ensure timers schema matches production column definitions.</intent>
    /// <scenario>Given the Timers table deployed in the infra schema.</scenario>
    /// <behavior>Then all timer and work-queue columns are present with correct types.</behavior>
    [Fact]
    public async Task TimersTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await GetTableColumnsAsync("infra", "Timers");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["DueTime"] = "datetimeoffset",
            ["Payload"] = "nvarchar",
            ["Topic"] = "nvarchar",
            ["CorrelationId"] = "nvarchar",
            ["StatusCode"] = "tinyint",
            ["LockedUntil"] = "datetime2",
            ["OwnerToken"] = "uniqueidentifier",
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

    /// <summary>When inspecting the JobRuns table, then required columns exist with expected data types.</summary>
    /// <intent>Ensure job run schema matches production column definitions.</intent>
    /// <scenario>Given the JobRuns table deployed in the infra schema.</scenario>
    /// <behavior>Then all job run and work-queue columns are present with correct types.</behavior>
    [Fact]
    public async Task JobRunsTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await GetTableColumnsAsync("infra", "JobRuns");

        // Assert
        var expectedColumns = new Dictionary<string, string>
(StringComparer.Ordinal)
        {
            ["Id"] = "uniqueidentifier",
            ["JobId"] = "uniqueidentifier",
            ["ScheduledTime"] = "datetimeoffset",
            ["StatusCode"] = "tinyint",
            ["LockedUntil"] = "datetime2",
            ["OwnerToken"] = "uniqueidentifier",
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

    /// <summary>When inspecting the Inbox table, then required columns exist with expected data types.</summary>
    /// <intent>Ensure inbox schema matches production column definitions.</intent>
    /// <scenario>Given the Inbox table deployed in the infra schema.</scenario>
    /// <behavior>Then all inbox columns are present with correct types.</behavior>
    [Fact]
    public async Task InboxTable_HasCorrectSchema()
    {
        // Arrange & Act
        var columns = await GetTableColumnsAsync("infra", "Inbox");

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

    /// <summary>When required indexes are inspected, then core tables have the expected indexes.</summary>
    /// <intent>Verify production index definitions are present in tests.</intent>
    /// <scenario>Given the infra schema with deployed tables and indexes.</scenario>
    /// <behavior>Then the outbox, jobs, timers, and job runs indexes exist.</behavior>
    [Fact]
    public async Task RequiredIndexes_ExistOnAllTables()
    {
        // Act & Assert
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Check critical indexes exist
        var indexExists = await IndexExistsAsync(connection, "infra", "Outbox", "IX_Outbox_WorkQueue");
        indexExists.ShouldBeTrue("Outbox should have IX_Outbox_WorkQueue index");

        indexExists = await IndexExistsAsync(connection, "infra", "Jobs", "UQ_Jobs_JobName");
        indexExists.ShouldBeTrue("Jobs should have UQ_Jobs_JobName index");

        indexExists = await IndexExistsAsync(connection, "infra", "Timers", "IX_Timers_WorkQueue");
        indexExists.ShouldBeTrue("Timers should have IX_Timers_WorkQueue index");

        indexExists = await IndexExistsAsync(connection, "infra", "JobRuns", "IX_JobRuns_WorkQueue");
        indexExists.ShouldBeTrue("JobRuns should have IX_JobRuns_WorkQueue index");
    }

    /// <summary>When a custom schema name is used, then scheduler tables and indexes are created in that schema.</summary>
    /// <intent>Ensure schema customization works for scheduler artifacts.</intent>
    /// <scenario>Given DatabaseSchemaManager.EnsureSchedulerSchemaAsync called with a custom schema and table names.</scenario>
    /// <behavior>Then the custom tables exist and the expected index name is present.</behavior>
    [Fact]
    public async Task CustomSchemaNames_WorkCorrectly()
    {
        // Arrange
        var customSchema = "custom_test";
        var customConnectionString = ConnectionString;

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

        var tablesExist = await TableExistsAsync(connection, customSchema, "CustomJobs");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobs should exist");

        tablesExist = await TableExistsAsync(connection, customSchema, "CustomJobRuns");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobRuns should exist");

        tablesExist = await TableExistsAsync(connection, customSchema, "CustomTimers");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomTimers should exist");

        // Check indexes have correct parameterized names
        var indexExists = await IndexExistsAsync(connection, customSchema, "CustomJobs", "UQ_CustomJobs_JobName");
        indexExists.ShouldBeTrue("Custom Jobs table should have correctly named unique index");
    }

    /// <summary>When work-queue migrations are applied, then work-queue columns and types exist.</summary>
    /// <intent>Verify work-queue migration adds required columns and table types.</intent>
    /// <scenario>Given the Outbox table after schema deployment.</scenario>
    /// <behavior>Then Status, LockedUntil, OwnerToken, and the GuidIdList type exist.</behavior>
    [Fact]
    public async Task WorkQueueColumns_ExistAfterMigration()
    {
        // Arrange & Act - WorkQueue migration should have been applied during setup
        var columns = await GetTableColumnsAsync("infra", "Outbox");

        // Assert - Work queue columns should exist
        columns.ShouldContainKey("Status", "Status column should exist after work queue migration");
        columns.ShouldContainKey("LockedUntil", "LockedUntil column should exist after work queue migration");
        columns.ShouldContainKey("OwnerToken", "OwnerToken column should exist after work queue migration");

        // Check that the type infra.GuidIdList exists
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var typeExists = await connection.QuerySingleOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM sys.types WHERE name = 'GuidIdList' AND schema_id = SCHEMA_ID('infra')");

        typeExists.ShouldBeGreaterThan(0, "Work queue type infra.GuidIdList should exist");
    }

    /// <summary>When work-queue procedures are inspected, then they use SYSUTCDATETIME for timing.</summary>
    /// <intent>Ensure stored procedures rely on database-authoritative time.</intent>
    /// <scenario>Given the Outbox and Inbox work-queue procedures deployed in infra.</scenario>
    /// <behavior>Then procedure definitions include SYSUTCDATETIME.</behavior>
    [Fact]
    public async Task WorkQueueProcedures_UseDatabaseUtcTime()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Check ReapExpired procedures
        var outboxReaper = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Outbox_ReapExpired'))");
        var inboxReaper = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Inbox_ReapExpired'))");

        outboxReaper.ShouldContain("SYSUTCDATETIME", Case.Sensitive);
        inboxReaper.ShouldContain("SYSUTCDATETIME", Case.Sensitive);

        // Check Claim procedures
        var outboxClaim = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Outbox_Claim'))");
        var inboxClaim = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Inbox_Claim'))");

        outboxClaim.ShouldContain("SYSUTCDATETIME", Case.Sensitive);
        inboxClaim.ShouldContain("SYSUTCDATETIME", Case.Sensitive);

        // Check Ack procedures
        var outboxAck = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Outbox_Ack'))");
        var inboxAck = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Inbox_Ack'))");

        outboxAck.ShouldContain("SYSUTCDATETIME", Case.Sensitive);
        inboxAck.ShouldContain("SYSUTCDATETIME", Case.Sensitive);

        // Check Abandon procedures
        var outboxAbandon = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Outbox_Abandon'))");
        var inboxAbandon = await connection.ExecuteScalarAsync<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('infra.Inbox_Abandon'))");

        outboxAbandon.ShouldContain("SYSUTCDATETIME", Case.Sensitive);
        inboxAbandon.ShouldContain("SYSUTCDATETIME", Case.Sensitive);
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
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

            var columns = await connection.QueryAsync<(string ColumnName, string DataType)>(
                sql, new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);

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
