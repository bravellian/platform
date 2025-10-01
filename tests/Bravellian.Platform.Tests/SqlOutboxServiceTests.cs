namespace Bravellian.Platform.Tests;

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data;

public class SqlOutboxServiceTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = "", SchemaName = "dbo", TableName = "Outbox" };

    public SqlOutboxServiceTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.defaultOptions.ConnectionString = this.ConnectionString;
        this.outboxService = new SqlOutboxService(Options.Create(this.defaultOptions), NullLogger<SqlOutboxService>.Instance);
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var service = new SqlOutboxService(Options.Create(this.defaultOptions), NullLogger<SqlOutboxService>.Instance);

        // Assert
        service.ShouldNotBeNull();
        service.ShouldBeAssignableTo<IOutbox>();
    }

    [Fact]
    public async Task EnqueueAsync_WithValidParameters_InsertsMessageToDatabase()
    {
        // Arrange
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic";
        string payload = "test payload";
        string correlationId = "test-correlation-123";

        // Act
        await this.outboxService!.EnqueueAsync(topic, payload, transaction, correlationId);

        // Verify the message was inserted
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        
        var count = (int)await command.ExecuteScalarAsync();

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
            ConnectionString = this.ConnectionString, 
            SchemaName = "custom", 
            TableName = "CustomOutbox", 
        };
        
        // Create the custom table for this test
        await using var setupConnection = new SqlConnection(this.ConnectionString);
        await setupConnection.OpenAsync();
        
        // Create custom schema if it doesn't exist
        await setupConnection.ExecuteAsync("IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'custom') EXEC('CREATE SCHEMA custom')");
        
        // Create custom table using DatabaseSchemaManager
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(this.ConnectionString, "custom", "CustomOutbox");
        
        var customOutboxService = new SqlOutboxService(Options.Create(customOptions), NullLogger<SqlOutboxService>.Instance);

        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-custom";
        string payload = "test payload custom";

        // Act
        await customOutboxService.EnqueueAsync(topic, payload, transaction);

        // Verify the message was inserted into the custom table
        var sql = "SELECT COUNT(*) FROM custom.CustomOutbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        
        var count = (int)await command.ExecuteScalarAsync();

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    [Fact]
    public async Task EnqueueAsync_WithNullCorrelationId_InsertsMessageSuccessfully()
    {
        // Arrange
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-null-correlation";
        string payload = "test payload with null correlation";

        // Act
        await this.outboxService!.EnqueueAsync(topic, payload, transaction, correlationId: null);

        // Verify the message was inserted
        var sql = "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic AND Payload = @Payload";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@Payload", payload);
        
        var count = (int)await command.ExecuteScalarAsync();

        // Assert
        count.ShouldBe(1);

        // Rollback to keep the test isolated
        transaction.Rollback();
    }

    [Fact]
    public async Task EnqueueAsync_WithValidParameters_SetsDefaultValues()
    {
        // Arrange
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        string topic = "test-topic-defaults";
        string payload = "test payload for defaults";

        try
        {
            // Act
            await this.outboxService!.EnqueueAsync(topic, payload, transaction);

            // Verify the message has correct default values
            var sql = @"SELECT IsProcessed, ProcessedAt, RetryCount, CreatedAt, MessageId 
                   FROM dbo.Outbox 
                   WHERE Topic = @Topic AND Payload = @Payload";
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Topic", topic);
            command.Parameters.AddWithValue("@Payload", payload);

            await using var reader = await command.ExecuteReaderAsync();
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
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        // Act - Insert multiple messages
        await this.outboxService!.EnqueueAsync("topic-1", "payload-1", transaction);
        await this.outboxService.EnqueueAsync("topic-2", "payload-2", transaction);
        await this.outboxService.EnqueueAsync("topic-3", "payload-3", transaction);

        // Verify all messages were inserted
        var sql = "SELECT COUNT(*) FROM dbo.Outbox";
        await using var command = new SqlCommand(sql, connection, transaction);
        var count = (int)await command.ExecuteScalarAsync();

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
            () => this.outboxService!.EnqueueAsync(validTopic, validPayload, nullTransaction));
        
        exception.ShouldNotBeNull();
    }
}