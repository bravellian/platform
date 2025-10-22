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
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

/// <summary>
/// Integration tests to verify that custom schemas (non-dbo) work correctly across all platform components.
/// These tests ensure that schema configuration is respected during deployment and at runtime.
/// </summary>
public class CustomSchemaIntegrationTests : SqlServerTestBase
{
    private const string CustomSchema = "platform";

    public CustomSchemaIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task EnsureDistributedLockSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "DistributedLock").ConfigureAwait(false);

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "DistributedLock").ConfigureAwait(false);
        Assert.True(tableExists, $"DistributedLock table should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var lockAcquireExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Acquire").ConfigureAwait(false);
        Assert.True(lockAcquireExists, $"Lock_Acquire procedure should exist in {CustomSchema} schema");

        var lockRenewExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Renew").ConfigureAwait(false);
        Assert.True(lockRenewExists, $"Lock_Renew procedure should exist in {CustomSchema} schema");

        var lockReleaseExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Release").ConfigureAwait(false);
        Assert.True(lockReleaseExists, $"Lock_Release procedure should exist in {CustomSchema} schema");

        var lockCleanupExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_CleanupExpired").ConfigureAwait(false);
        Assert.True(lockCleanupExists, $"Lock_CleanupExpired procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureLeaseSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Lease").ConfigureAwait(false);

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "Lease").ConfigureAwait(false);
        Assert.True(tableExists, $"Lease table should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var leaseAcquireExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lease_Acquire").ConfigureAwait(false);
        Assert.True(leaseAcquireExists, $"Lease_Acquire procedure should exist in {CustomSchema} schema");

        var leaseRenewExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lease_Renew").ConfigureAwait(false);
        Assert.True(leaseRenewExists, $"Lease_Renew procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureOutboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Outbox").ConfigureAwait(false);

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var outboxExists = await this.TableExistsAsync(connection, CustomSchema, "Outbox").ConfigureAwait(false);
        Assert.True(outboxExists, $"Outbox table should exist in {CustomSchema} schema");

        var stateExists = await this.TableExistsAsync(connection, CustomSchema, "OutboxState").ConfigureAwait(false);
        Assert.True(stateExists, $"OutboxState table should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureInboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Inbox").ConfigureAwait(false);

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "Inbox").ConfigureAwait(false);
        Assert.True(tableExists, $"Inbox table should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureSchedulerSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Jobs",
            "JobRuns",
            "Timers").ConfigureAwait(false);

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var jobsExists = await this.TableExistsAsync(connection, CustomSchema, "Jobs").ConfigureAwait(false);
        Assert.True(jobsExists, $"Jobs table should exist in {CustomSchema} schema");

        var timersExists = await this.TableExistsAsync(connection, CustomSchema, "Timers").ConfigureAwait(false);
        Assert.True(timersExists, $"Timers table should exist in {CustomSchema} schema");

        var jobRunsExists = await this.TableExistsAsync(connection, CustomSchema, "JobRuns").ConfigureAwait(false);
        Assert.True(jobRunsExists, $"JobRuns table should exist in {CustomSchema} schema");

        var stateExists = await this.TableExistsAsync(connection, CustomSchema, "SchedulerState").ConfigureAwait(false);
        Assert.True(stateExists, $"SchedulerState table should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange - First create the Outbox table that the work queue extends
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Outbox").ConfigureAwait(false);

        // Act
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            this.ConnectionString,
            CustomSchema).ConfigureAwait(false);

        // Assert - Verify type exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var typeExists = await this.TypeExistsAsync(connection, CustomSchema, "GuidIdList").ConfigureAwait(false);
        Assert.True(typeExists, $"GuidIdList type should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var claimExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Claim").ConfigureAwait(false);
        Assert.True(claimExists, $"Outbox_Claim procedure should exist in {CustomSchema} schema");

        var ackExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Ack").ConfigureAwait(false);
        Assert.True(ackExists, $"Outbox_Ack procedure should exist in {CustomSchema} schema");

        var abandonExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Abandon").ConfigureAwait(false);
        Assert.True(abandonExists, $"Outbox_Abandon procedure should exist in {CustomSchema} schema");

        var failExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Fail").ConfigureAwait(false);
        Assert.True(failExists, $"Outbox_Fail procedure should exist in {CustomSchema} schema");

        var reapExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_ReapExpired").ConfigureAwait(false);
        Assert.True(reapExists, $"Outbox_ReapExpired procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureInboxWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange - First create the Inbox table that the work queue extends
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Inbox").ConfigureAwait(false);

        // Act
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            this.ConnectionString,
            CustomSchema).ConfigureAwait(false);

        // Assert - Verify type exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var typeExists = await this.TypeExistsAsync(connection, CustomSchema, "StringIdList").ConfigureAwait(false);
        Assert.True(typeExists, $"StringIdList type should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var claimExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Claim").ConfigureAwait(false);
        Assert.True(claimExists, $"Inbox_Claim procedure should exist in {CustomSchema} schema");

        var ackExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Ack").ConfigureAwait(false);
        Assert.True(ackExists, $"Inbox_Ack procedure should exist in {CustomSchema} schema");

        var abandonExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Abandon").ConfigureAwait(false);
        Assert.True(abandonExists, $"Inbox_Abandon procedure should exist in {CustomSchema} schema");

        var failExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Fail").ConfigureAwait(false);
        Assert.True(failExists, $"Inbox_Fail procedure should exist in {CustomSchema} schema");

        var reapExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_ReapExpired").ConfigureAwait(false);
        Assert.True(reapExists, $"Inbox_ReapExpired procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureFanoutSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "FanoutPolicy",
            "FanoutCursor").ConfigureAwait(false);

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var policyExists = await this.TableExistsAsync(connection, CustomSchema, "FanoutPolicy").ConfigureAwait(false);
        Assert.True(policyExists, $"FanoutPolicy table should exist in {CustomSchema} schema");

        var cursorExists = await this.TableExistsAsync(connection, CustomSchema, "FanoutCursor").ConfigureAwait(false);
        Assert.True(cursorExists, $"FanoutCursor table should exist in {CustomSchema} schema");
    }

    private async Task<bool> TableExistsAsync(SqlConnection connection, string schemaName, string tableName)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@TableName", tableName);

        var count = (int)await command.ExecuteScalarAsync().ConfigureAwait(false);
        return count > 0;
    }

    private async Task<bool> StoredProcedureExistsAsync(SqlConnection connection, string schemaName, string procedureName)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_SCHEMA = @SchemaName AND ROUTINE_NAME = @ProcedureName AND ROUTINE_TYPE = 'PROCEDURE'
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@ProcedureName", procedureName);

        var count = (int)await command.ExecuteScalarAsync().ConfigureAwait(false);
        return count > 0;
    }

    private async Task<bool> TypeExistsAsync(SqlConnection connection, string schemaName, string typeName)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.types t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @SchemaName AND t.name = @TypeName AND t.is_table_type = 1
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SchemaName", schemaName);
        command.Parameters.AddWithValue("@TypeName", typeName);

        var count = (int)await command.ExecuteScalarAsync().ConfigureAwait(false);
        return count > 0;
    }
}
