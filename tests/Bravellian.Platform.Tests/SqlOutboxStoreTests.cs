namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.SqlClient;
using System.Data;
using Dapper;
using System.Linq;

public class SqlOutboxStoreTests : SqlServerTestBase
{
    private SqlOutboxStore? outboxStore;
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = "", SchemaName = "dbo", TableName = "Outbox" };
    private FakeTimeProvider timeProvider = default!;

    public SqlOutboxStoreTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.timeProvider = new FakeTimeProvider();
        this.defaultOptions.ConnectionString = this.ConnectionString;
        var logger = new TestLogger<SqlOutboxStore>(this.TestOutputHelper);
        this.outboxStore = new SqlOutboxStore(Options.Create(this.defaultOptions), this.timeProvider, logger);
    }

    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        // Act
        var messages = await this.outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimDueAsync_WithDueMessages_ReturnsMessages()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync($@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1), // Due in the past
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        // Act
        var messages = await this.outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(1);
        messages.First().Id.ShouldBe(messageId);
        messages.First().Topic.ShouldBe("Test.Topic");
        messages.First().Payload.ShouldBe("test payload");
    }

    [Fact]
    public async Task ClaimDueAsync_WithFutureMessages_ReturnsEmpty()
    {
        // Arrange - Add a message scheduled for the future
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync($@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10), // Due in the future
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        var messages = await this.outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task MarkDispatchedAsync_UpdatesMessage()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync($@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        await this.outboxStore!.MarkDispatchedAsync(messageId, CancellationToken.None);

        // Assert - Check the message is marked as processed
        var processed = await connection.QueryFirstAsync<bool>($@"
            SELECT IsProcessed FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        processed.ShouldBeTrue();
    }

    [Fact]
    public async Task RescheduleAsync_UpdatesRetryCountAndNextAttempt()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync($@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 2)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var delay = TimeSpan.FromMinutes(5);
        var errorMessage = "Test error";

        // Act
        await this.outboxStore!.RescheduleAsync(messageId, delay, errorMessage, CancellationToken.None);

        // Assert - Check the message is updated
        var result = await connection.QueryFirstAsync($@"
            SELECT RetryCount, LastError, NextAttemptAt FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((int)result.RetryCount).ShouldBe(3); // Should be incremented from 2 to 3
        ((string)result.LastError).ShouldBe(errorMessage);

        var nextAttempt = (DateTimeOffset)result.NextAttemptAt;
        nextAttempt.ShouldBeGreaterThan(this.timeProvider.GetUtcNow().Add(delay).AddMinutes(-1));
        nextAttempt.ShouldBeLessThan(this.timeProvider.GetUtcNow().Add(delay).AddMinutes(1));
    }

    [Fact]
    public async Task FailAsync_MarksMessageAsFailed()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync($@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var errorMessage = "Permanent failure";

        // Act
        await this.outboxStore!.FailAsync(messageId, errorMessage, CancellationToken.None);

        // Assert - Check the message is marked as processed with error
        var result = await connection.QueryFirstAsync($@"
            SELECT IsProcessed, LastError, ProcessedBy FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((bool)result.IsProcessed).ShouldBeTrue();
        ((string)result.LastError).ShouldBe(errorMessage);
        ((string)result.ProcessedBy).ShouldContain("FAILED");
    }
}