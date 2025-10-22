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

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;

public class InboxDispatcherTests : SqlServerTestBase
{
    public InboxDispatcherTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure inbox work queue schema is set up (stored procedures and types)
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(this.ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task RunOnceAsync_WithNoMessages_ReturnsZero()
    {
        // Arrange
        var store = this.CreateInboxWorkStore();
        var resolver = this.CreateHandlerResolver();
        var dispatcher = this.CreateDispatcher(store, resolver);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(0, processedCount);
    }

    [Fact]
    public async Task RunOnceAsync_WithValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var inbox = this.CreateInboxService();
        var store = this.CreateInboxWorkStore();
        var resolver = this.CreateHandlerResolver(new TestInboxHandler("test-topic"));
        var dispatcher = this.CreateDispatcher(store, resolver);

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
        var inbox = this.CreateInboxService();
        var store = this.CreateInboxWorkStore();
        var resolver = this.CreateHandlerResolver(); // No handlers registered
        var dispatcher = this.CreateDispatcher(store, resolver);

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
        var inbox = this.CreateInboxService();
        var store = this.CreateInboxWorkStore();
        var failingHandler = new FailingInboxHandler("failing-topic", shouldFail: true);
        var resolver = this.CreateHandlerResolver(failingHandler);
        var dispatcher = this.CreateDispatcher(store, resolver);

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
            TableName = "Inbox",
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
            TableName = "Inbox",
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
            this.Topic = topic;
        }

        public string Topic { get; }

        public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
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
            this.Topic = topic;
            this.shouldFail = shouldFail;
        }

        public string Topic { get; }

        public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
        {
            if (this.shouldFail)
            {
                throw new InvalidOperationException("Simulated handler failure");
            }

            return Task.CompletedTask;
        }
    }
}
