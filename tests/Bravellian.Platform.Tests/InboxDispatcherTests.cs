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


using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxDispatcherTests : SqlServerTestBase
{
    public InboxDispatcherTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure inbox work queue schema is set up (stored procedures and types)
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);
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
        await inbox.EnqueueAsync("test-topic", "test-source", "msg-1", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Done
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

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
        await inbox.EnqueueAsync("unknown-topic", "test-source", "msg-2", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Dead due to no handler
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

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
        await inbox.EnqueueAsync("failing-topic", "test-source", "msg-3", "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act - First attempt should fail and abandon
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was abandoned (back to Seen status)
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-3" });

        Assert.Equal("Seen", status);
    }

    private SqlInboxService CreateInboxService()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxService>(TestOutputHelper);
        return new SqlInboxService(options, logger);
    }

    private SqlInboxWorkStore CreateInboxWorkStore()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxWorkStore>(TestOutputHelper);
        return new SqlInboxWorkStore(options, TimeProvider.System, logger);
    }

    private InboxHandlerResolver CreateHandlerResolver(params IInboxHandler[] handlers)
    {
        return new InboxHandlerResolver(handlers);
    }

    private MultiInboxDispatcher CreateDispatcher(IInboxWorkStore store, IInboxHandlerResolver resolver)
    {
        var provider = new SingleInboxWorkStoreProvider(store);
        var logger = new TestLogger<MultiInboxDispatcher>(TestOutputHelper);
        var strategy = new RoundRobinInboxSelectionStrategy();
        return new MultiInboxDispatcher(provider, strategy, resolver, logger);
    }

    private sealed class SingleInboxWorkStoreProvider : IInboxWorkStoreProvider
    {
        private readonly IInboxWorkStore store;

        public SingleInboxWorkStoreProvider(IInboxWorkStore store)
        {
            this.store = store;
        }

        public Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync() =>
            Task.FromResult<IReadOnlyList<IInboxWorkStore>>(new[] { store });

        public string GetStoreIdentifier(IInboxWorkStore store) => "default";

        public IInboxWorkStore? GetStoreByKey(string key) => store;

        public IInbox? GetInboxByKey(string key) => null;
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
            Topic = topic;
            this.shouldFail = shouldFail;
        }

        public string Topic { get; }

        public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
        {
            if (shouldFail)
            {
                throw new InvalidOperationException("Simulated handler failure");
            }

            return Task.CompletedTask;
        }
    }
}
