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
using Npgsql;

namespace Bravellian.Platform.Tests;

/// <summary>
/// Tests to ensure that the PostgreSQL schema used in tests is consistent with production schema.
/// This prevents issues where test schemas diverge from production schemas.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class DatabaseSchemaConsistencyTests : PostgresTestBase
{
    public DatabaseSchemaConsistencyTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task DatabaseSchema_AllRequiredTablesExist()
    {
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

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var tableName in expectedTables)
        {
            var exists = await TableExistsAsync(connection, "infra", tableName);
            exists.ShouldBeTrue($"Table infra.{tableName} should exist");
        }
    }

    [Fact]
    public async Task OutboxTable_HasCorrectSchema()
    {
        var columns = await GetTableColumnsAsync("infra", "Outbox");

        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = "uuid",
            ["Payload"] = "text",
            ["Topic"] = "text",
            ["CreatedAt"] = "timestamp with time zone",
            ["IsProcessed"] = "boolean",
            ["ProcessedAt"] = "timestamp with time zone",
            ["ProcessedBy"] = "text",
            ["RetryCount"] = "integer",
            ["LastError"] = "text",
            ["MessageId"] = "uuid",
            ["CorrelationId"] = "text",
            ["DueTimeUtc"] = "timestamp with time zone",
            ["Status"] = "smallint",
            ["LockedUntil"] = "timestamp with time zone",
            ["OwnerToken"] = "uuid",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Outbox table");
            columns[columnName].ShouldBe(expectedType);
        }
    }

    [Fact]
    public async Task JobsTable_HasCorrectSchema()
    {
        var columns = await GetTableColumnsAsync("infra", "Jobs");

        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = "uuid",
            ["JobName"] = "character varying",
            ["CronSchedule"] = "character varying",
            ["Topic"] = "text",
            ["Payload"] = "text",
            ["IsEnabled"] = "boolean",
            ["NextDueTime"] = "timestamp with time zone",
            ["LastRunTime"] = "timestamp with time zone",
            ["LastRunStatus"] = "character varying",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Jobs table");
            columns[columnName].ShouldBe(expectedType);
        }
    }

    [Fact]
    public async Task TimersTable_HasCorrectSchema()
    {
        var columns = await GetTableColumnsAsync("infra", "Timers");

        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = "uuid",
            ["DueTime"] = "timestamp with time zone",
            ["Payload"] = "text",
            ["Topic"] = "text",
            ["CorrelationId"] = "text",
            ["StatusCode"] = "smallint",
            ["LockedUntil"] = "timestamp with time zone",
            ["OwnerToken"] = "uuid",
            ["Status"] = "character varying",
            ["ClaimedBy"] = "character varying",
            ["ClaimedAt"] = "timestamp with time zone",
            ["RetryCount"] = "integer",
            ["CreatedAt"] = "timestamp with time zone",
            ["ProcessedAt"] = "timestamp with time zone",
            ["LastError"] = "text",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Timers table");
            columns[columnName].ShouldBe(expectedType);
        }
    }

    [Fact]
    public async Task JobRunsTable_HasCorrectSchema()
    {
        var columns = await GetTableColumnsAsync("infra", "JobRuns");

        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = "uuid",
            ["JobId"] = "uuid",
            ["ScheduledTime"] = "timestamp with time zone",
            ["StatusCode"] = "smallint",
            ["LockedUntil"] = "timestamp with time zone",
            ["OwnerToken"] = "uuid",
            ["Status"] = "character varying",
            ["ClaimedBy"] = "character varying",
            ["ClaimedAt"] = "timestamp with time zone",
            ["RetryCount"] = "integer",
            ["StartTime"] = "timestamp with time zone",
            ["EndTime"] = "timestamp with time zone",
            ["Output"] = "text",
            ["LastError"] = "text",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in JobRuns table");
            columns[columnName].ShouldBe(expectedType);
        }
    }

    [Fact]
    public async Task InboxTable_HasCorrectSchema()
    {
        var columns = await GetTableColumnsAsync("infra", "Inbox");

        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MessageId"] = "character varying",
            ["Source"] = "character varying",
            ["Hash"] = "bytea",
            ["FirstSeenUtc"] = "timestamp with time zone",
            ["LastSeenUtc"] = "timestamp with time zone",
            ["ProcessedUtc"] = "timestamp with time zone",
            ["DueTimeUtc"] = "timestamp with time zone",
            ["Attempts"] = "integer",
            ["Status"] = "character varying",
            ["LastError"] = "text",
            ["LockedUntil"] = "timestamp with time zone",
            ["OwnerToken"] = "uuid",
            ["Topic"] = "character varying",
            ["Payload"] = "text",
        };

        foreach (var (columnName, expectedType) in expectedColumns)
        {
            columns.ShouldContainKey(columnName, $"Column {columnName} should exist in Inbox table");
            columns[columnName].ShouldBe(expectedType);
        }
    }

    [Fact]
    public async Task DatabaseSchema_RequiredIndexesExist()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var indexExists = await IndexExistsAsync(connection, "infra", "Outbox", "IX_Outbox_WorkQueue");
        indexExists.ShouldBeTrue("Outbox should have IX_Outbox_WorkQueue index");

        indexExists = await IndexExistsAsync(connection, "infra", "Inbox", "IX_Inbox_WorkQueue");
        indexExists.ShouldBeTrue("Inbox should have IX_Inbox_WorkQueue index");

        indexExists = await IndexExistsAsync(connection, "infra", "Inbox", "IX_Inbox_Status");
        indexExists.ShouldBeTrue("Inbox should have IX_Inbox_Status index");

        indexExists = await IndexExistsAsync(connection, "infra", "Inbox", "IX_Inbox_ProcessedUtc");
        indexExists.ShouldBeTrue("Inbox should have IX_Inbox_ProcessedUtc index");

        indexExists = await IndexExistsAsync(connection, "infra", "Inbox", "IX_Inbox_Status_ProcessedUtc");
        indexExists.ShouldBeTrue("Inbox should have IX_Inbox_Status_ProcessedUtc index");

        indexExists = await IndexExistsAsync(connection, "infra", "Jobs", "UQ_Jobs_JobName");
        indexExists.ShouldBeTrue("Jobs should have UQ_Jobs_JobName index");

        indexExists = await IndexExistsAsync(connection, "infra", "Timers", "IX_Timers_WorkQueue");
        indexExists.ShouldBeTrue("Timers should have IX_Timers_WorkQueue index");

        indexExists = await IndexExistsAsync(connection, "infra", "JobRuns", "IX_JobRuns_WorkQueue");
        indexExists.ShouldBeTrue("JobRuns should have IX_JobRuns_WorkQueue index");
    }

    [Fact]
    public async Task CustomSchemaNames_WorkCorrectly()
    {
        var customSchema = "custom_test";

        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            ConnectionString,
            customSchema,
            "CustomJobs",
            "CustomJobRuns",
            "CustomTimers");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tablesExist = await TableExistsAsync(connection, customSchema, "CustomJobs");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobs should exist");

        tablesExist = await TableExistsAsync(connection, customSchema, "CustomJobRuns");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomJobRuns should exist");

        tablesExist = await TableExistsAsync(connection, customSchema, "CustomTimers");
        tablesExist.ShouldBeTrue($"Custom table {customSchema}.CustomTimers should exist");

        var indexExists = await IndexExistsAsync(connection, customSchema, "CustomJobs", "UQ_CustomJobs_JobName");
        indexExists.ShouldBeTrue("Custom Jobs table should have correctly named unique index");
    }

    [Fact]
    public async Task WorkQueueColumns_ExistAfterMigration()
    {
        var columns = await GetTableColumnsAsync("infra", "Outbox");

        columns.ShouldContainKey("Status", "Status column should exist after work queue migration");
        columns.ShouldContainKey("LockedUntil", "LockedUntil column should exist after work queue migration");
        columns.ShouldContainKey("OwnerToken", "OwnerToken column should exist after work queue migration");
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string schemaName, string tableName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @SchemaName AND table_name = @TableName
            """;

        var count = await connection.QuerySingleAsync<int>(
            sql, new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);
        return count > 0;
    }

    private async Task<Dictionary<string, string>> GetTableColumnsAsync(string schemaName, string tableName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = @SchemaName AND table_name = @TableName
            """;

        var columns = await connection.QueryAsync<(string ColumnName, string DataType)>(
            sql, new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);

        return columns.ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.Ordinal);
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string schemaName, string tableName, string indexName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = @SchemaName
              AND tablename = @TableName
              AND indexname = @IndexName
            """;

        var count = await connection.QuerySingleAsync<int>(
            sql, new { SchemaName = schemaName, TableName = tableName, IndexName = indexName }).ConfigureAwait(false);
        return count > 0;
    }
}
