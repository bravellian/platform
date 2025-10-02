namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;

public class InboxWorkStoreTests : SqlServerTestBase
{
    public InboxWorkStoreTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task ClaimAsync_WithNoMessages_ReturnsEmpty()
    {
        // Arrange
        var store = CreateInboxWorkStore();
        var ownerToken = Guid.NewGuid();

        // Act
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Empty(claimedIds);
    }

    [Fact]
    public async Task ClaimAsync_WithAvailableMessage_ClaimsSuccessfully()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = Guid.NewGuid();

        // Enqueue a test message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");

        // Act
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Single(claimedIds);
        Assert.Contains("msg-1", claimedIds);

        // Verify message status is Processing and has owner token
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Processing", result.Status);
        Assert.Equal(ownerToken, result.OwnerToken);
    }

    [Fact]
    public async Task ClaimAsync_WithConcurrentWorkers_EnsuresExclusiveClaims()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();

        // Enqueue a single message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");

        // Act - Two workers try to claim the same message
        var claims1Task = store.ClaimAsync(owner1, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        var claims2Task = store.ClaimAsync(owner2, leaseSeconds: 30, batchSize: 10, CancellationToken.None);

        var claims1 = await claims1Task;
        var claims2 = await claims2Task;

        // Assert - Only one worker should get the message
        var totalClaimed = claims1.Count + claims2.Count;
        Assert.Equal(1, totalClaimed);
        
        // Verify exactly one claim succeeded
        Assert.True((claims1.Count == 1 && claims2.Count == 0) || (claims1.Count == 0 && claims2.Count == 1));
    }

    [Fact]
    public async Task AckAsync_WithClaimedMessage_MarksAsDone()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = Guid.NewGuid();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AckAsync(ownerToken, claimedIds, CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken, ProcessedUtc FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Done", result.Status);
        Assert.Null(result.OwnerToken);
        Assert.NotNull(result.ProcessedUtc);
    }

    [Fact]
    public async Task AbandonAsync_WithClaimedMessage_ReturnsToSeen()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = Guid.NewGuid();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.AbandonAsync(ownerToken, claimedIds, CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Seen", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task FailAsync_WithClaimedMessage_MarksAsDead()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var ownerToken = Guid.NewGuid();

        // Enqueue and claim a message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");
        var claimedIds = await store.ClaimAsync(ownerToken, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act
        await store.FailAsync(ownerToken, claimedIds, "Test failure", CancellationToken.None);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Dead", result.Status);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public async Task OwnerTokenEnforcement_OnlyAllowsOperationsByOwner()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var rightOwner = Guid.NewGuid();
        var wrongOwner = Guid.NewGuid();

        // Enqueue and claim a message with rightOwner
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");
        var claimedIds = await store.ClaimAsync(rightOwner, leaseSeconds: 30, batchSize: 10, CancellationToken.None);
        Assert.Single(claimedIds);

        // Act - Try to ack with wrong owner
        await store.AckAsync(wrongOwner, claimedIds, CancellationToken.None);

        // Assert - Message should still be Processing (ack should have been ignored)
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var result = await connection.QuerySingleAsync(
            "SELECT Status, OwnerToken FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Processing", result.Status);
        Assert.Equal(rightOwner, result.OwnerToken);
    }

    [Fact]
    public async Task GetAsync_WithValidMessageId_ReturnsMessage()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();

        // Enqueue a test message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");

        // Act
        var message = await store.GetAsync("msg-1", CancellationToken.None);

        // Assert
        Assert.NotNull(message);
        Assert.Equal("msg-1", message.MessageId);
        Assert.Equal("test-source", message.Source);
        Assert.Equal("test-topic", message.Topic);
        Assert.Equal("test payload", message.Payload);
        Assert.Equal(1, message.Attempt); // First attempt
    }

    [Fact]
    public async Task GetAsync_WithInvalidMessageId_ThrowsException()
    {
        // Arrange
        var store = CreateInboxWorkStore();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync("non-existent", CancellationToken.None));
    }

    private SqlInboxService CreateInboxService()
    {
        var options = Options.Create(new SqlInboxOptions 
        { 
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox"
        });
        
        var logger = new TestLogger<SqlInboxService>(this.TestOutputHelper);
        return new SqlInboxService(options, logger);
    }

    private SqlInboxWorkStore CreateInboxWorkStore()
    {
        var options = Options.Create(new SqlInboxOptions 
        { 
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox"
        });
        
        var logger = new TestLogger<SqlInboxWorkStore>(this.TestOutputHelper);
        return new SqlInboxWorkStore(options, logger);
    }
}