namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;

public class InboxDispatcherTests : SqlServerTestBase
{
    public InboxDispatcherTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task RunOnceAsync_WithNoMessages_ReturnsZero()
    {
        // Arrange
        var store = CreateInboxWorkStore();
        var resolver = CreateHandlerResolver();
        var dispatcher = CreateDispatcher(store, resolver);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(0, processedCount);
    }

    [Fact]
    public async Task RunOnceAsync_WithValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var resolver = CreateHandlerResolver(new TestInboxHandler("test-topic"));
        var dispatcher = CreateDispatcher(store, resolver);

        // Enqueue a test message
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload");

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Done
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-1" });
        
        Assert.Equal("Done", status);
    }

    [Fact]
    public async Task RunOnceAsync_WithNoHandlerForTopic_MarksMessageAsDead()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var resolver = CreateHandlerResolver(); // No handlers registered
        var dispatcher = CreateDispatcher(store, resolver);

        // Enqueue a test message with unknown topic
        await inbox.EnqueueAsync("unknown-topic", "test-source", "msg-2", "test payload");

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Dead due to no handler
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-2" });
        
        Assert.Equal("Dead", status);
    }

    [Fact]
    public async Task RunOnceAsync_WithFailingHandler_RetriesWithBackoff()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var failingHandler = new FailingInboxHandler("failing-topic", shouldFail: true);
        var resolver = CreateHandlerResolver(failingHandler);
        var dispatcher = CreateDispatcher(store, resolver);

        // Enqueue a test message
        await inbox.EnqueueAsync("failing-topic", "test-source", "msg-3", "test payload");

        // Act - First attempt should fail and abandon
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was abandoned (back to Seen status)
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        
        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-3" });
        
        Assert.Equal("Seen", status);
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

    private InboxHandlerResolver CreateHandlerResolver(params IInboxHandler[] handlers)
    {
        return new InboxHandlerResolver(handlers);
    }

    private InboxDispatcher CreateDispatcher(IInboxWorkStore store, IInboxHandlerResolver resolver)
    {
        var logger = new TestLogger<InboxDispatcher>(this.TestOutputHelper);
        return new InboxDispatcher(store, resolver, logger);
    }

    /// <summary>
    /// Test handler that always succeeds.
    /// </summary>
    private class TestInboxHandler : IInboxHandler
    {
        public TestInboxHandler(string topic)
        {
            Topic = topic;
        }

        public string Topic { get; }

        public Task HandleAsync(IInboxMessage message, CancellationToken cancellationToken)
        {
            // Just succeed
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test handler that can be configured to fail.
    /// </summary>
    private class FailingInboxHandler : IInboxHandler
    {
        private readonly bool shouldFail;

        public FailingInboxHandler(string topic, bool shouldFail)
        {
            Topic = topic;
            this.shouldFail = shouldFail;
        }

        public string Topic { get; }

        public Task HandleAsync(IInboxMessage message, CancellationToken cancellationToken)
        {
            if (this.shouldFail)
            {
                throw new InvalidOperationException("Simulated handler failure");
            }

            return Task.CompletedTask;
        }
    }
}