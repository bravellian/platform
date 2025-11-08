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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Integration tests to verify that custom schemas (non-dbo) work correctly across all platform components.
/// These tests ensure that schema configuration is respected during deployment and at runtime.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class CustomSchemaIntegrationTests : SqlServerTestBase
{
    private const string CustomSchema = "platform";

    public CustomSchemaIntegrationTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    [Fact]
    public async Task EnsureDistributedLockSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "DistributedLock");

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "DistributedLock");
        Assert.True(tableExists, $"DistributedLock table should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var lockAcquireExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Acquire");
        Assert.True(lockAcquireExists, $"Lock_Acquire procedure should exist in {CustomSchema} schema");

        var lockRenewExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Renew");
        Assert.True(lockRenewExists, $"Lock_Renew procedure should exist in {CustomSchema} schema");

        var lockReleaseExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_Release");
        Assert.True(lockReleaseExists, $"Lock_Release procedure should exist in {CustomSchema} schema");

        var lockCleanupExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lock_CleanupExpired");
        Assert.True(lockCleanupExists, $"Lock_CleanupExpired procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureLeaseSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Lease");

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "Lease");
        Assert.True(tableExists, $"Lease table should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var leaseAcquireExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lease_Acquire");
        Assert.True(leaseAcquireExists, $"Lease_Acquire procedure should exist in {CustomSchema} schema");

        var leaseRenewExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Lease_Renew");
        Assert.True(leaseRenewExists, $"Lease_Renew procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureOutboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Outbox");

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var outboxExists = await this.TableExistsAsync(connection, CustomSchema, "Outbox");
        Assert.True(outboxExists, $"Outbox table should exist in {CustomSchema} schema");

        var stateExists = await this.TableExistsAsync(connection, CustomSchema, "OutboxState");
        Assert.True(stateExists, $"OutboxState table should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureInboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange & Act
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Inbox");

        // Assert - Verify table exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await this.TableExistsAsync(connection, CustomSchema, "Inbox");
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
            "Timers");

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var jobsExists = await this.TableExistsAsync(connection, CustomSchema, "Jobs");
        Assert.True(jobsExists, $"Jobs table should exist in {CustomSchema} schema");

        var timersExists = await this.TableExistsAsync(connection, CustomSchema, "Timers");
        Assert.True(timersExists, $"Timers table should exist in {CustomSchema} schema");

        var jobRunsExists = await this.TableExistsAsync(connection, CustomSchema, "JobRuns");
        Assert.True(jobRunsExists, $"JobRuns table should exist in {CustomSchema} schema");

        var stateExists = await this.TableExistsAsync(connection, CustomSchema, "SchedulerState");
        Assert.True(stateExists, $"SchedulerState table should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange - First create the Outbox table that the work queue extends
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Outbox");

        // Act
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            this.ConnectionString,
            CustomSchema);

        // Assert - Verify type exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var typeExists = await this.TypeExistsAsync(connection, CustomSchema, "GuidIdList");
        Assert.True(typeExists, $"GuidIdList type should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var claimExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Claim");
        Assert.True(claimExists, $"Outbox_Claim procedure should exist in {CustomSchema} schema");

        var ackExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Ack");
        Assert.True(ackExists, $"Outbox_Ack procedure should exist in {CustomSchema} schema");

        var abandonExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Abandon");
        Assert.True(abandonExists, $"Outbox_Abandon procedure should exist in {CustomSchema} schema");

        var failExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_Fail");
        Assert.True(failExists, $"Outbox_Fail procedure should exist in {CustomSchema} schema");

        var reapExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Outbox_ReapExpired");
        Assert.True(reapExists, $"Outbox_ReapExpired procedure should exist in {CustomSchema} schema");
    }

    [Fact]
    public async Task EnsureInboxWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        // Arrange - First create the Inbox table that the work queue extends
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            this.ConnectionString,
            CustomSchema,
            "Inbox");

        // Act
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            this.ConnectionString,
            CustomSchema);

        // Assert - Verify type exists in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var typeExists = await this.TypeExistsAsync(connection, CustomSchema, "StringIdList");
        Assert.True(typeExists, $"StringIdList type should exist in {CustomSchema} schema");

        // Verify stored procedures exist in custom schema
        var claimExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Claim");
        Assert.True(claimExists, $"Inbox_Claim procedure should exist in {CustomSchema} schema");

        var ackExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Ack");
        Assert.True(ackExists, $"Inbox_Ack procedure should exist in {CustomSchema} schema");

        var abandonExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Abandon");
        Assert.True(abandonExists, $"Inbox_Abandon procedure should exist in {CustomSchema} schema");

        var failExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_Fail");
        Assert.True(failExists, $"Inbox_Fail procedure should exist in {CustomSchema} schema");

        var reapExists = await this.StoredProcedureExistsAsync(connection, CustomSchema, "Inbox_ReapExpired");
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
            "FanoutCursor");

        // Assert - Verify tables exist in custom schema
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var policyExists = await this.TableExistsAsync(connection, CustomSchema, "FanoutPolicy");
        Assert.True(policyExists, $"FanoutPolicy table should exist in {CustomSchema} schema");

        var cursorExists = await this.TableExistsAsync(connection, CustomSchema, "FanoutCursor");
        Assert.True(cursorExists, $"FanoutCursor table should exist in {CustomSchema} schema");
    }

    [Fact]
    public void AddSqlScheduler_WithCustomSchema_RegistersLeaseFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new SqlSchedulerOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = CustomSchema,
            EnableSchemaDeployment = false, // Prevent actual deployment during test
        };

        // Act
        services.AddSqlScheduler(options);

        // Assert - Verify that ISystemLeaseFactory is registered (which means AddSystemLeases was called)
        var leaseFactoryDescriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(ISystemLeaseFactory));

        Assert.NotNull(leaseFactoryDescriptor);

        // Verify that SystemLeaseOptions configuration was registered
        var optionsDescriptor = services.FirstOrDefault(s =>
            s.ServiceType == typeof(IConfigureOptions<SystemLeaseOptions>));

        Assert.NotNull(optionsDescriptor);
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

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
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

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
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

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return count > 0;
    }
}
