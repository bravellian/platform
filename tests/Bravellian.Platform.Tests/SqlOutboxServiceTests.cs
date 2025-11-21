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
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "dbo", TableName = "Outbox" };

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

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var service = new SqlOutboxService(Options.Create(defaultOptions), NullLogger<SqlOutboxService>.Instance);

        // Assert
        service.ShouldNotBeNull();
        service.ShouldBeAssignableTo<IOutbox>();
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

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
                   FROM dbo.Outbox 
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
            reader.GetDateTimeOffset(3).ShouldBeGreaterThan(DateTimeOffset.Now.AddMinutes(-1)); // CreatedAt
            reader.GetGuid(4).ShouldNotBe(Guid.Empty); // MessageId
        }
        finally
        {
            // Rollback to keep the test isolated
            transaction.Rollback();
        }
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Outbox";
        await using var command = new SqlCommand(sql, connection, transaction);
        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(3);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    [Fact]
    public async Task EnqueueAsync_WithNullTransaction_ThrowsNullReferenceException()
    {
        // Arrange
        IDbTransaction nullTransaction = null!;
        string validTopic = "test-topic";
        string validPayload = "test payload";

        // Act & Assert
        // The implementation tries to access transaction.Connection without checking null
        var exception = await Should.ThrowAsync<NullReferenceException>(
            () => outboxService!.EnqueueAsync(validTopic, validPayload, nullTransaction, CancellationToken.None));

        exception.ShouldNotBeNull();
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload AND CorrelationId = @CorrelationId";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@CorrelationId", correlationId);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Clean up
        var deleteSql = "DELETE FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var deleteCommand = new SqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert
        count.ShouldBe(1);

        // Clean up
        var deleteSql = "DELETE FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var deleteCommand = new SqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@Topic", topic);
        deleteCommand.Parameters.AddWithValue("@Payload", payload);
        await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

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
            var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic LIKE @TopicPattern";
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
            var deleteSql = "DELETE FROM dbo.Outbox WHERE Topic LIKE @TopicPattern";
            await using var deleteCommand = new SqlCommand(deleteSql, connection);
            deleteCommand.Parameters.AddWithValue("@TopicPattern", $"%-{testId}");
            await deleteCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task EnqueueAsync_Standalone_EnsuresTableExists()
    {
        // Arrange - Create a custom outbox service with a different table name
        var customOptions = new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            TableName = "TestOutbox_StandaloneEnsure",
        };

        var customOutboxService = new SqlOutboxService(Options.Create(customOptions), NullLogger<SqlOutboxService>.Instance);

        // First, ensure the custom table doesn't exist
        await using var setupConnection = new SqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync("IF OBJECT_ID('dbo.TestOutbox_StandaloneEnsure', 'U') IS NOT NULL DROP TABLE dbo.TestOutbox_StandaloneEnsure");

        string topic = "test-topic-ensure";
        string payload = "test payload ensure";

        try
        {
            // Act - This should create the table and insert the message
            await customOutboxService.EnqueueAsync(topic, payload, CancellationToken.None);

            // Verify the table was created and message was inserted
            var sql = "SELECT COUNT(*) FROM dbo.TestOutbox_StandaloneEnsure WHERE Topic = @Topic AND Payload = @Payload";
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
            await setupConnection.ExecuteAsync("IF OBJECT_ID('dbo.TestOutbox_StandaloneEnsure', 'U') IS NOT NULL DROP TABLE dbo.TestOutbox_StandaloneEnsure");
        }
    }
}
