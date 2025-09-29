namespace Bravellian.Platform.Tests;

using System.Data;
using Microsoft.Data.SqlClient;

public class SqlOutboxServiceTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;

    public SqlOutboxServiceTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.outboxService = new SqlOutboxService();
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var service = new SqlOutboxService();

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