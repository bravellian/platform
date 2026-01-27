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

using System.Data;
using Dapper;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class PostgresOutboxServiceTests : PostgresTestBase
{
    private PostgresOutboxService? outboxService;
    private readonly PostgresOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox" };
    private string qualifiedTableName = string.Empty;

    public PostgresOutboxServiceTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        qualifiedTableName = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);
        outboxService = new PostgresOutboxService(Options.Create(defaultOptions), NullLogger<PostgresOutboxService>.Instance);
    }

    /// <summary>When constructed with valid options, then the service is created and implements IOutbox.</summary>
    /// <intent>Verify the outbox service can be instantiated with valid options.</intent>
    /// <scenario>Given PostgresOutboxOptions and a null logger instance.</scenario>
    /// <behavior>The instance is non-null and assignable to IOutbox.</behavior>
    [Fact]
    public void Constructor_CreatesInstance()
    {
        var service = new PostgresOutboxService(Options.Create(defaultOptions), NullLogger<PostgresOutboxService>.Instance);

        service.ShouldNotBeNull();
        service.ShouldBeAssignableTo<IOutbox>();
    }

    /// <summary>When EnqueueAsync is called in a transaction, then a message row is inserted.</summary>
    /// <intent>Verify transactional enqueue inserts a row with topic and payload.</intent>
    /// <scenario>Given an open connection, a transaction, and topic/payload/correlation id values.</scenario>
    /// <behavior>The outbox table contains one row matching the topic and payload.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithValidParameters_InsertsMessageToDatabase()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic";
        string payload = "test payload";
        string correlationId = "test-correlation-123";

        await outboxService!.EnqueueAsync(topic, payload, transaction, correlationId, CancellationToken.None);

        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(1);

        transaction.Rollback();
    }

    /// <summary>When using a custom schema and table, then EnqueueAsync inserts into the custom table.</summary>
    /// <intent>Verify custom schema/table options are honored by enqueue.</intent>
    /// <scenario>Given a custom outbox service and a transaction scoped to the custom table.</scenario>
    /// <behavior>The custom outbox table contains one row matching the topic and payload.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithCustomSchemaAndTable_InsertsMessageToCorrectTable()
    {
        var customOptions = new PostgresOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "custom",
            TableName = "CustomOutbox",
            EnableSchemaDeployment = false,
        };

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, customOptions.SchemaName, customOptions.TableName);

        var customOutboxService = new PostgresOutboxService(Options.Create(customOptions), NullLogger<PostgresOutboxService>.Instance);
        var customTable = PostgresSqlHelper.Qualify(customOptions.SchemaName, customOptions.TableName);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-custom";
        string payload = "test payload custom";

        await customOutboxService.EnqueueAsync(topic, payload, transaction, CancellationToken.None);

        var sql = $"SELECT COUNT(*) FROM {customTable} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(1);

        transaction.Rollback();
    }

    /// <summary>When EnqueueAsync is called with a null correlation id, then the message is inserted.</summary>
    /// <intent>Verify null correlation ids do not block insertion.</intent>
    /// <scenario>Given a transaction and a message with a null correlation id.</scenario>
    /// <behavior>The outbox table contains one row matching the topic and payload.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithNullCorrelationId_InsertsMessageSuccessfully()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-null-correlation";
        string payload = "test payload with null correlation";

        await outboxService!.EnqueueAsync(topic, payload, transaction, correlationId: null, cancellationToken: CancellationToken.None);

        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(1);

        transaction.Rollback();
    }

    /// <summary>When EnqueueAsync inserts a message, then default fields are set.</summary>
    /// <intent>Verify default outbox values are populated on insert.</intent>
    /// <scenario>Given a new message enqueued in a transaction.</scenario>
    /// <behavior>IsProcessed is false, ProcessedAt is null, RetryCount is 0, CreatedAt is recent, and MessageId is set.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithValidParameters_SetsDefaultValues()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-defaults";
        string payload = "test payload for defaults";

        try
        {
            await outboxService!.EnqueueAsync(topic, payload, transaction, CancellationToken.None);

            var sql = $"SELECT \"IsProcessed\", \"ProcessedAt\", \"RetryCount\", \"CreatedAt\", \"MessageId\" FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Topic", topic);
            command.Parameters.AddWithValue("@Payload", payload);

            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read().ShouldBeTrue();

            reader.GetBoolean(0).ShouldBe(false);
            reader.IsDBNull(1).ShouldBeTrue();
            reader.GetInt32(2).ShouldBe(0);
            reader.GetFieldValue<DateTimeOffset>(3).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
            reader.GetGuid(4).ShouldNotBe(Guid.Empty);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    /// <summary>When multiple messages are enqueued in a transaction, then all rows are inserted.</summary>
    /// <intent>Verify batch enqueues insert all messages within the transaction.</intent>
    /// <scenario>Given three enqueue calls in the same transaction.</scenario>
    /// <behavior>The outbox table contains three rows.</behavior>
    [Fact]
    public async Task EnqueueAsync_MultipleMessages_AllInsertedSuccessfully()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        await outboxService!.EnqueueAsync("topic-1", "payload-1", transaction, CancellationToken.None);
        await outboxService.EnqueueAsync("topic-2", "payload-2", transaction, CancellationToken.None);
        await outboxService.EnqueueAsync("topic-3", "payload-3", transaction, CancellationToken.None);

        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName}";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(3);

        transaction.Rollback();
    }

    /// <summary>When EnqueueAsync receives a null transaction, then it throws ArgumentNullException.</summary>
    /// <intent>Verify EnqueueAsync validates the transaction argument.</intent>
    /// <scenario>Given a null IDbTransaction argument.</scenario>
    /// <behavior>EnqueueAsync throws ArgumentNullException.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithNullTransaction_ThrowsNullReferenceException()
    {
        IDbTransaction nullTransaction = null!;
        string validTopic = "test-topic";
        string validPayload = "test payload";

        var exception = await Should.ThrowAsync<ArgumentNullException>(
            () => outboxService!.EnqueueAsync(validTopic, validPayload, nullTransaction, CancellationToken.None));

        exception.ShouldNotBeNull();
    }

    /// <summary>When using standalone enqueue, then the message is inserted with the correlation id.</summary>
    /// <intent>Verify standalone enqueue persists messages without an explicit transaction.</intent>
    /// <scenario>Given a topic, payload, and correlation id.</scenario>
    /// <behavior>The outbox table contains one row matching the topic, payload, and correlation id.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_WithValidParameters_InsertsMessageToDatabase()
    {
        string topic = "test-topic-standalone";
        string payload = "test payload standalone";
        string correlationId = "test-correlation-standalone";

        await outboxService!.EnqueueAsync(topic, payload, correlationId, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload AND \"CorrelationId\" = @CorrelationId";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@CorrelationId", correlationId);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(1);

        var deleteSql = $"DELETE FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var deleteCommand = new NpgsqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>When standalone enqueue uses a null correlation id, then the message is inserted.</summary>
    /// <intent>Verify standalone enqueue handles null correlation ids.</intent>
    /// <scenario>Given a topic and payload with a null correlation id.</scenario>
    /// <behavior>The outbox table contains one row matching the topic and payload.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_WithNullCorrelationId_InsertsMessageSuccessfully()
    {
        string topic = "test-topic-standalone-null";
        string payload = "test payload standalone null";

        await outboxService!.EnqueueAsync(topic, payload, (string?)null, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

        count.ShouldBe(1);

        var deleteSql = $"DELETE FROM {qualifiedTableName} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
        await using var deleteCommand = new NpgsqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>When multiple standalone enqueue calls are made, then all rows are inserted.</summary>
    /// <intent>Verify multiple standalone enqueues insert all messages.</intent>
    /// <scenario>Given three distinct topics/payloads enqueued without a transaction.</scenario>
    /// <behavior>The outbox table contains three matching rows.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_MultipleMessages_AllInsertedSuccessfully()
    {
        var testId = Guid.NewGuid().ToString("N");
        var topics = new[] { $"topic-1-{testId}", $"topic-2-{testId}", $"topic-3-{testId}" };
        var payloads = new[] { $"payload-1-{testId}", $"payload-2-{testId}", $"payload-3-{testId}" };

        try
        {
            await outboxService!.EnqueueAsync(topics[0], payloads[0], CancellationToken.None);
            await outboxService.EnqueueAsync(topics[1], payloads[1], CancellationToken.None);
            await outboxService.EnqueueAsync(topics[2], payloads[2], CancellationToken.None);

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var sql = $"SELECT COUNT(*) FROM {qualifiedTableName} WHERE \"Topic\" LIKE @TopicPattern";
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TopicPattern", $"%-{testId}");
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

            count.ShouldBe(3);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var deleteSql = $"DELETE FROM {qualifiedTableName} WHERE \"Topic\" LIKE @TopicPattern";
            await using var deleteCommand = new NpgsqlCommand(deleteSql, connection);
            deleteCommand.Parameters.AddWithValue("@TopicPattern", $"%-{testId}");
            await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>When standalone enqueue runs with schema deployment enabled, then it creates the table and inserts the row.</summary>
    /// <intent>Verify schema deployment ensures the outbox table exists before enqueue.</intent>
    /// <scenario>Given a custom schema/table with EnableSchemaDeployment true and the table dropped.</scenario>
    /// <behavior>The custom outbox table is created and contains the enqueued message.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_EnsuresTableExists()
    {
        var customOptions = new PostgresOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra_ensure",
            TableName = "TestOutbox_StandaloneEnsure",
            EnableSchemaDeployment = true,
        };

        var customOutboxService = new PostgresOutboxService(Options.Create(customOptions), NullLogger<PostgresOutboxService>.Instance);
        var customTable = PostgresSqlHelper.Qualify(customOptions.SchemaName, customOptions.TableName);

        await using var setupConnection = new NpgsqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS {PostgresSqlHelper.QuoteIdentifier(customOptions.SchemaName)}");
        await setupConnection.ExecuteAsync($"DROP TABLE IF EXISTS {customTable}");

        string topic = "test-topic-ensure";
        string payload = "test payload ensure";

        try
        {
            await customOutboxService.EnqueueAsync(topic, payload, CancellationToken.None);

            var sql = $"SELECT COUNT(*) FROM {customTable} WHERE \"Topic\" = @Topic AND \"Payload\" = @Payload";
            await using var command = new NpgsqlCommand(sql, setupConnection);
            command.Parameters.AddWithValue("@Topic", topic);
            command.Parameters.AddWithValue("@Payload", payload);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);

            count.ShouldBe(1);
        }
        finally
        {
            await setupConnection.ExecuteAsync($"DROP TABLE IF EXISTS {customTable}");
        }
    }
}

