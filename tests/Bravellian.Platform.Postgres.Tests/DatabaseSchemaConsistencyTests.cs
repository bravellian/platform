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
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureMetricsSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureCentralMetricsSchemaAsync(ConnectionString, "control").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureIdempotencySchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOperationsSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureAuditSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureEmailOutboxSchemaAsync(ConnectionString).ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureEmailDeliverySchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>Given core schemas are deployed, then all required tables exist.</summary>
    /// <intent>Verify core schema deployment creates the expected tables.</intent>
    /// <scenario>Given Outbox, Inbox, Scheduler, and Fanout schemas ensured in the infra schema.</scenario>
    /// <behavior>Each expected core table is present in infra.</behavior>
    [Fact]
    public async Task DatabaseSchema_AllRequiredTablesExist()
    {
        var expectedTables = new[]
        {
            "Outbox",
            "OutboxState",
            "OutboxJoin",
            "OutboxJoinMember",
            "Inbox",
            "Jobs",
            "JobRuns",
            "Timers",
            "SchedulerState",
            "FanoutPolicy",
            "FanoutCursor",
            "Lease",
            "DistributedLock",
            "MetricDef",
            "MetricSeries",
            "MetricPointMinute",
            "Idempotency",
            "Operations",
            "OperationEvents",
            "AuditEvents",
            "AuditAnchors",
            "EmailOutbox",
            "EmailDeliveryEvents",
        };

        var expectedControlTables = new[]
        {
            "MetricDef",
            "MetricSeries",
            "MetricPointHourly",
            "ExporterHeartbeat",
        };

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        foreach (var tableName in expectedTables)
        {
            var exists = await TableExistsAsync(connection, "infra", tableName);
            exists.ShouldBeTrue($"Table infra.{tableName} should exist");
        }

        foreach (var tableName in expectedControlTables)
        {
            var exists = await TableExistsAsync(connection, "control", tableName);
            exists.ShouldBeTrue($"Table control.{tableName} should exist");
        }
    }

    /// <summary>When inspecting the Outbox table, then all required columns and types match.</summary>
    /// <intent>Validate the Outbox table shape matches the expected schema.</intent>
    /// <scenario>Given the infra.Outbox table created by schema deployment.</scenario>
    /// <behavior>Each required Outbox column exists with the expected PostgreSQL data type.</behavior>
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

    /// <summary>When inspecting the Jobs table, then all required columns and types match.</summary>
    /// <intent>Validate the Jobs table shape matches the expected schema.</intent>
    /// <scenario>Given the infra.Jobs table created by schema deployment.</scenario>
    /// <behavior>Each required Jobs column exists with the expected PostgreSQL data type.</behavior>
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

    /// <summary>When inspecting the Timers table, then all required columns and types match.</summary>
    /// <intent>Validate the Timers table shape matches the expected schema.</intent>
    /// <scenario>Given the infra.Timers table created by schema deployment.</scenario>
    /// <behavior>Each required Timers column exists with the expected PostgreSQL data type.</behavior>
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

    /// <summary>When inspecting the JobRuns table, then all required columns and types match.</summary>
    /// <intent>Validate the JobRuns table shape matches the expected schema.</intent>
    /// <scenario>Given the infra.JobRuns table created by schema deployment.</scenario>
    /// <behavior>Each required JobRuns column exists with the expected PostgreSQL data type.</behavior>
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

    /// <summary>When inspecting the Inbox table, then all required columns and types match.</summary>
    /// <intent>Validate the Inbox table shape matches the expected schema.</intent>
    /// <scenario>Given the infra.Inbox table created by schema deployment.</scenario>
    /// <behavior>Each required Inbox column exists with the expected PostgreSQL data type.</behavior>
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

    /// <summary>When verifying schema indexes, then required work-queue and uniqueness indexes exist.</summary>
    /// <intent>Validate required indexes are created for core tables.</intent>
    /// <scenario>Given the infra schema with Outbox, Inbox, Jobs, Timers, and JobRuns tables.</scenario>
    /// <behavior>All expected indexes are present in PostgreSQL metadata.</behavior>
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

    /// <summary>When creating scheduler tables in a custom schema, then tables and indexes use the custom names.</summary>
    /// <intent>Verify scheduler schema respects custom schema and table names.</intent>
    /// <scenario>Given EnsureSchedulerSchemaAsync is called with schema custom_test and custom table names.</scenario>
    /// <behavior>CustomJobs, CustomJobRuns, and CustomTimers tables exist with the expected index name.</behavior>
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

    /// <summary>When checking the Outbox table after work-queue migration, then work-queue columns exist.</summary>
    /// <intent>Confirm work-queue columns are present after migration.</intent>
    /// <scenario>Given the infra.Outbox table created by schema deployment.</scenario>
    /// <behavior>Status, LockedUntil, and OwnerToken columns are present.</behavior>
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

