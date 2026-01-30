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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SqlOutboxServiceTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox" };

    public SqlOutboxServiceTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        outboxService = new SqlOutboxService(Options.Create(defaultOptions), NullLogger<SqlOutboxService>.Instance);
    }

    /// <summary>When constructing the outbox service, then it provides an IOutbox implementation.</summary>
    /// <intent>Verify SqlOutboxService instantiation succeeds.</intent>
    /// <scenario>Create a SqlOutboxService with default options and a null logger.</scenario>
    /// <behavior>The service is not null and is assignable to IOutbox.</behavior>
    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var service = new SqlOutboxService(Options.Create(defaultOptions), NullLogger<SqlOutboxService>.Instance);

        // Assert
        service.ShouldNotBeNull();
        service.ShouldBeAssignableTo<IOutbox>();
    }

    /// <summary>When enqueueing with a transaction, then a matching outbox row is inserted.</summary>
    /// <intent>Validate enqueue persists messages within a transaction.</intent>
    /// <scenario>Open a SQL transaction, enqueue a message with topic, payload, and correlation id.</scenario>
    /// <behavior>The outbox table contains exactly one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithValidParameters_InsertsMessageToDatabase()
    {
        // Arrange
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic";
        string payload = "test payload";
        string correlationId = "test-correlation-123";

        // Act
        await outboxService!.EnqueueAsync(topic, payload, transaction, correlationId, CancellationToken.None);

        // Verify the message was inserted
        var sql = "SELECT COUNT(*) FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    /// <summary>When using custom schema and table options, then the message is inserted into that table.</summary>
    /// <intent>Ensure custom outbox table routing is honored.</intent>
    /// <scenario>Create a custom schema/table and enqueue a message using a custom outbox service.</scenario>
    /// <behavior>The custom table contains one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithCustomSchemaAndTable_InsertsMessageToCorrectTable()
    {
        // Arrange - Use custom schema and table name
        var customOptions = new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "custom",
            TableName = "CustomOutbox",
        };

        // Create the custom table for this test
        await using var setupConnection = new SqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);

        // Create custom schema if it doesn't exist
        await setupConnection.ExecuteAsync("IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'custom') EXEC('CREATE SCHEMA custom')");

        // Create custom table using DatabaseSchemaManager
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "custom", "CustomOutbox");

        var customOutboxService = new SqlOutboxService(Options.Create(customOptions), NullLogger<SqlOutboxService>.Instance);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-custom";
        string payload = "test payload custom";

        // Act
        await customOutboxService.EnqueueAsync(topic, payload, transaction, CancellationToken.None);

        // Verify the message was inserted into the custom table
        var sql = "SELECT COUNT(*) FROM custom.CustomOutbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    /// <summary>When enqueueing with a null correlation id, then the message is still inserted.</summary>
    /// <intent>Allow null correlation ids during transactional enqueue.</intent>
    /// <scenario>Enqueue a message with topic and payload and a null correlation id.</scenario>
    /// <behavior>The outbox table contains one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithNullCorrelationId_InsertsMessageSuccessfully()
    {
        // Arrange
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-null-correlation";
        string payload = "test payload with null correlation";

        // Act
        await outboxService!.EnqueueAsync(topic, payload, transaction, correlationId: null, cancellationToken: CancellationToken.None);

        // Verify the message was inserted
        var sql = "SELECT COUNT(*) FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    /// <summary>When enqueueing a message, then default outbox columns are initialized.</summary>
    /// <intent>Verify insert defaults for processing metadata.</intent>
    /// <scenario>Enqueue a message in a transaction and read the inserted row.</scenario>
    /// <behavior>IsProcessed is false, ProcessedAt is null, RetryCount is 0, CreatedAt is recent, and MessageId is set.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithValidParameters_SetsDefaultValues()
    {
        // Arrange
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-defaults";
        string payload = "test payload for defaults";

        try
        {
            // Act
            await outboxService!.EnqueueAsync(topic, payload, transaction, CancellationToken.None);

            // Verify the message has correct default values
            var sql = @"SELECT IsProcessed, ProcessedAt, RetryCount, CreatedAt, MessageId
                   FROM infra.Outbox
                   WHERE Topic = @Topic AND Payload = @Payload";
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Topic", topic);
            command.Parameters.AddWithValue("@Payload", payload);

            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read().ShouldBeTrue();

            // Assert default values
            reader.GetBoolean(0).ShouldBe(false); // IsProcessed
            reader.IsDBNull(1).ShouldBeTrue(); // ProcessedAt
            reader.GetInt32(2).ShouldBe(0); // RetryCount
            reader.GetDateTimeOffset(3).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)); // CreatedAt
            reader.GetGuid(4).ShouldNotBe(Guid.Empty); // MessageId
        }
        finally
        {
            // Rollback to keep the test isolated
            transaction.Rollback();
        }
    }

    /// <summary>When enqueueing multiple messages in one transaction, then all rows are inserted.</summary>
    /// <intent>Verify batch inserts within a transaction.</intent>
    /// <scenario>Enqueue three messages using the same transaction.</scenario>
    /// <behavior>The outbox table contains three rows.</behavior>
    [Fact]
    public async Task EnqueueAsync_MultipleMessages_AllInsertedSuccessfully()
    {
        // Arrange
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = connection.BeginTransaction();

        // Act - Insert multiple messages
        await outboxService!.EnqueueAsync("topic-1", "payload-1", transaction, CancellationToken.None);
        await outboxService.EnqueueAsync("topic-2", "payload-2", transaction, CancellationToken.None);
        await outboxService.EnqueueAsync("topic-3", "payload-3", transaction, CancellationToken.None);

        // Verify all messages were inserted
        var sql = "SELECT COUNT(*) FROM infra.Outbox";
        await using var command = new SqlCommand(sql, connection, transaction);
        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(3);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    /// <summary>When the transaction is null, then EnqueueAsync throws ArgumentNullException.</summary>
    /// <intent>Enforce a non-null transaction for transactional enqueue.</intent>
    /// <scenario>Call EnqueueAsync with a null IDbTransaction.</scenario>
    /// <behavior>An ArgumentNullException is thrown.</behavior>
    [Fact]
    public async Task EnqueueAsync_WithNullTransaction_ThrowsNullReferenceException()
    {
        // Arrange
        IDbTransaction nullTransaction = null!;
        string validTopic = "test-topic";
        string validPayload = "test payload";

        // Act & Assert
        // The implementation tries to access transaction.Connection without checking null
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            () => outboxService!.EnqueueAsync(validTopic, validPayload, nullTransaction, CancellationToken.None));

        exception.ShouldNotBeNull();
    }

    /// <summary>When enqueueing without an explicit transaction, then the message is inserted.</summary>
    /// <intent>Verify standalone enqueue creates an outbox record.</intent>
    /// <scenario>Call EnqueueAsync with topic, payload, and correlation id.</scenario>
    /// <behavior>The outbox table contains one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_WithValidParameters_InsertsMessageToDatabase()
    {
        // Arrange
        string topic = "test-topic-standalone";
        string payload = "test payload standalone";
        string correlationId = "test-correlation-standalone";

        // Act
        await outboxService!.EnqueueAsync(topic, payload, correlationId, CancellationToken.None);

        // Verify the message was inserted by querying the database directly
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT COUNT(*) FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload AND CorrelationId = @CorrelationId";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@CorrelationId", correlationId);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Clean up
        var deleteSql = "DELETE FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var deleteCommand = new SqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>When standalone enqueue uses a null correlation id, then the message is inserted.</summary>
    /// <intent>Allow null correlation ids in standalone enqueue.</intent>
    /// <scenario>Call EnqueueAsync with topic, payload, and a null correlation id.</scenario>
    /// <behavior>The outbox table contains one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_WithNullCorrelationId_InsertsMessageSuccessfully()
    {
        // Arrange
        string topic = "test-topic-standalone-null";
        string payload = "test payload standalone null";

        // Act
        await outboxService!.EnqueueAsync(topic, payload, (string?)null, CancellationToken.None);

        // Verify the message was inserted
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT COUNT(*) FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Clean up
        var deleteSql = "DELETE FROM infra.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var deleteCommand = new SqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>When enqueueing multiple standalone messages, then all rows are inserted.</summary>
    /// <intent>Verify standalone enqueue handles multiple writes.</intent>
    /// <scenario>Enqueue three messages with unique topic/payload suffixes.</scenario>
    /// <behavior>The outbox table contains three matching rows.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_MultipleMessages_AllInsertedSuccessfully()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N");
        var topics = new[] { $"topic-1-{testId}", $"topic-2-{testId}", $"topic-3-{testId}" };
        var payloads = new[] { $"payload-1-{testId}", $"payload-2-{testId}", $"payload-3-{testId}" };

        try
        {
            // Act - Insert multiple messages using standalone method
            await outboxService!.EnqueueAsync(topics[0], payloads[0], CancellationToken.None);
            await outboxService.EnqueueAsync(topics[1], payloads[1], CancellationToken.None);
            await outboxService.EnqueueAsync(topics[2], payloads[2], CancellationToken.None);

            // Verify all messages were inserted
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var sql = "SELECT COUNT(*) FROM infra.Outbox WHERE Topic LIKE @TopicPattern";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TopicPattern", $"%-{testId}");
            var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            // Assert
            count.ShouldBe(3);
        }
        finally
        {
            // Clean up
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var deleteSql = "DELETE FROM infra.Outbox WHERE Topic LIKE @TopicPattern";
            await using var deleteCommand = new SqlCommand(deleteSql, connection);
            deleteCommand.Parameters.AddWithValue("@TopicPattern", $"%-{testId}");
            await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>When standalone enqueue targets a missing table, then the table is created and the message is inserted.</summary>
    /// <intent>Ensure standalone enqueue creates the outbox table if needed.</intent>
    /// <scenario>Drop the custom table, then enqueue using a service pointing at that table.</scenario>
    /// <behavior>The custom table exists and contains one matching row.</behavior>
    [Fact]
    public async Task EnqueueAsync_Standalone_EnsuresTableExists()
    {
        // Arrange - Create a custom outbox service with a different table name
        var customOptions = new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "TestOutbox_StandaloneEnsure",
        };

        var customOutboxService = new SqlOutboxService(Options.Create(customOptions), NullLogger<SqlOutboxService>.Instance);

        // First, ensure the custom table doesn't exist
        await using var setupConnection = new SqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync("IF OBJECT_ID('infra.TestOutbox_StandaloneEnsure', 'U') IS NOT NULL DROP TABLE infra.TestOutbox_StandaloneEnsure");

        string topic = "test-topic-ensure";
        string payload = "test payload ensure";

        try
        {
            // Act - This should create the table and insert the message
            await customOutboxService.EnqueueAsync(topic, payload, CancellationToken.None);

            // Verify the table was created and message was inserted
            var sql = "SELECT COUNT(*) FROM infra.TestOutbox_StandaloneEnsure WHERE Topic = @Topic AND Payload = @Payload";
            await using var command = new SqlCommand(sql, setupConnection);
            command.Parameters.AddWithValue("@Topic", topic);
            command.Parameters.AddWithValue("@Payload", payload);

            var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            // Assert
            count.ShouldBe(1);
        }
        finally
        {
            // Clean up - Drop the test table
            await setupConnection.ExecuteAsync("IF OBJECT_ID('infra.TestOutbox_StandaloneEnsure', 'U') IS NOT NULL DROP TABLE infra.TestOutbox_StandaloneEnsure");
        }
    }
}

