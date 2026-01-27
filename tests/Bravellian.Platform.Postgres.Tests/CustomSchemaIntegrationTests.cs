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
/// Integration tests to verify that custom schemas (non-infra) work correctly across all platform components.
/// These tests ensure that schema configuration is respected during deployment and at runtime.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class CustomSchemaIntegrationTests : PostgresTestBase
{
    private const string CustomSchema = "platform";

    public CustomSchemaIntegrationTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    /// <summary>
    /// When the distributed lock schema is ensured with a custom schema, then the table is created there.
    /// </summary>
    /// <intent>
    /// Verify distributed lock deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureDistributedLockSchemaAsync is called with the "platform" schema and table name "DistributedLock".
    /// </scenario>
    /// <behavior>
    /// The DistributedLock table is present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureDistributedLockSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            ConnectionString,
            CustomSchema,
            "DistributedLock");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await TableExistsAsync(connection, CustomSchema, "DistributedLock");
        Assert.True(tableExists, $"DistributedLock table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the lease schema is ensured with a custom schema, then the lease table is created there.
    /// </summary>
    /// <intent>
    /// Verify lease deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureLeaseSchemaAsync is called with the "platform" schema and table name "Lease".
    /// </scenario>
    /// <behavior>
    /// The Lease table is present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureLeaseSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Lease");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await TableExistsAsync(connection, CustomSchema, "Lease");
        Assert.True(tableExists, $"Lease table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the outbox schema is ensured with a custom schema, then outbox tables are created there.
    /// </summary>
    /// <intent>
    /// Verify outbox deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureOutboxSchemaAsync is called with the "platform" schema and table name "Outbox".
    /// </scenario>
    /// <behavior>
    /// The Outbox and OutboxState tables are present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureOutboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Outbox");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var outboxExists = await TableExistsAsync(connection, CustomSchema, "Outbox");
        Assert.True(outboxExists, $"Outbox table should exist in {CustomSchema} schema");

        var stateExists = await TableExistsAsync(connection, CustomSchema, "OutboxState");
        Assert.True(stateExists, $"OutboxState table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the inbox schema is ensured with a custom schema, then the Inbox table is created there.
    /// </summary>
    /// <intent>
    /// Verify inbox deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureInboxSchemaAsync is called with the "platform" schema and table name "Inbox".
    /// </scenario>
    /// <behavior>
    /// The Inbox table is present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureInboxSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Inbox");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tableExists = await TableExistsAsync(connection, CustomSchema, "Inbox");
        Assert.True(tableExists, $"Inbox table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the scheduler schema is ensured with a custom schema, then scheduler tables are created there.
    /// </summary>
    /// <intent>
    /// Verify scheduler deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureSchedulerSchemaAsync is called with the "platform" schema and custom table names.
    /// </scenario>
    /// <behavior>
    /// Jobs, Timers, JobRuns, and SchedulerState tables are present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureSchedulerSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Jobs",
            "JobRuns",
            "Timers");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var jobsExists = await TableExistsAsync(connection, CustomSchema, "Jobs");
        Assert.True(jobsExists, $"Jobs table should exist in {CustomSchema} schema");

        var timersExists = await TableExistsAsync(connection, CustomSchema, "Timers");
        Assert.True(timersExists, $"Timers table should exist in {CustomSchema} schema");

        var jobRunsExists = await TableExistsAsync(connection, CustomSchema, "JobRuns");
        Assert.True(jobRunsExists, $"JobRuns table should exist in {CustomSchema} schema");

        var stateExists = await TableExistsAsync(connection, CustomSchema, "SchedulerState");
        Assert.True(stateExists, $"SchedulerState table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the work-queue schema is ensured with a custom schema, then work-queue objects use that schema.
    /// </summary>
    /// <intent>
    /// Verify work-queue deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureOutboxSchemaAsync and EnsureWorkQueueSchemaAsync are called with the "platform" schema.
    /// </scenario>
    /// <behavior>
    /// The Outbox table is present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Outbox");

        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
            ConnectionString,
            CustomSchema);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var outboxExists = await TableExistsAsync(connection, CustomSchema, "Outbox");
        Assert.True(outboxExists, $"Outbox table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the inbox work-queue schema is ensured with a custom schema, then inbox objects are created there.
    /// </summary>
    /// <intent>
    /// Verify inbox work-queue deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureInboxSchemaAsync and EnsureInboxWorkQueueSchemaAsync are called with the "platform" schema.
    /// </scenario>
    /// <behavior>
    /// The Inbox table is present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureInboxWorkQueueSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(
            ConnectionString,
            CustomSchema,
            "Inbox");

        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(
            ConnectionString,
            CustomSchema);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var inboxExists = await TableExistsAsync(connection, CustomSchema, "Inbox");
        Assert.True(inboxExists, $"Inbox table should exist in {CustomSchema} schema");
    }

    /// <summary>
    /// When the fanout schema is ensured with a custom schema, then policy and cursor tables are created there.
    /// </summary>
    /// <intent>
    /// Verify fanout deployment honors the configured schema name.
    /// </intent>
    /// <scenario>
    /// Given EnsureFanoutSchemaAsync is called with the "platform" schema and custom table names.
    /// </scenario>
    /// <behavior>
    /// FanoutPolicy and FanoutCursor tables are present in the custom schema.
    /// </behavior>
    [Fact]
    public async Task EnsureFanoutSchema_WithCustomSchema_CreatesObjectsInCorrectSchema()
    {
        await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
            ConnectionString,
            CustomSchema,
            "FanoutPolicy",
            "FanoutCursor");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var policyExists = await TableExistsAsync(connection, CustomSchema, "FanoutPolicy");
        Assert.True(policyExists, $"FanoutPolicy table should exist in {CustomSchema} schema");

        var cursorExists = await TableExistsAsync(connection, CustomSchema, "FanoutCursor");
        Assert.True(cursorExists, $"FanoutCursor table should exist in {CustomSchema} schema");
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string schemaName, string tableName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @SchemaName AND table_name = @TableName
            """;

        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);
        return count > 0;
    }
}

