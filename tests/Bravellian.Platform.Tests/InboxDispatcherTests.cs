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
using Shouldly;

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
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
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
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
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
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = "msg-3" });

        Assert.Equal("Seen", status);
    }

    [Fact]
    public async Task RunOnceAsync_WithFailingHandler_AbandonsWithBackoffPolicy()
    {
        // Arrange
        var backoffDelay = TimeSpan.FromSeconds(5);
        var failingHandler = new FailingInboxHandler("failing-topic", shouldFail: true);
        var resolver = CreateHandlerResolver(failingHandler);
        var store = new StubInboxWorkStore();
        store.AddMessage("msg-stub-1", attempt: 0, topic: "failing-topic");

        var dispatcher = new MultiInboxDispatcher(
            new StubInboxWorkStoreProvider(store),
            new RoundRobinInboxSelectionStrategy(),
            resolver,
            new TestLogger<MultiInboxDispatcher>(TestOutputHelper),
            backoffPolicy: _ => backoffDelay,
            maxAttempts: 3);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        store.AbandonedMessages.ShouldHaveSingleItem();
        store.AbandonedMessages[0].Delay.ShouldBe(backoffDelay);
        store.FailedMessages.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WithPoisonMessage_FailsInsteadOfRetrying()
    {
        // Arrange
        var failingHandler = new FailingInboxHandler("failing-topic", shouldFail: true);
        var resolver = CreateHandlerResolver(failingHandler);
        var store = new StubInboxWorkStore();
        // Attempt is the number of previous processing attempts. The dispatcher calculates the next
        // attempt as (attempt + 1) and compares it to maxAttempts. With attempt = 5 and maxAttempts = 5,
        // the condition (attempt + 1 > maxAttempts) is true (6 > 5), so this message should be marked as failed.
        store.AddMessage("msg-stub-2", attempt: 5, topic: "failing-topic");

        var dispatcher = new MultiInboxDispatcher(
            new StubInboxWorkStoreProvider(store),
            new RoundRobinInboxSelectionStrategy(),
            resolver,
            new TestLogger<MultiInboxDispatcher>(TestOutputHelper),
            maxAttempts: 5);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        store.FailedMessages.ShouldContain("msg-stub-2");
        store.AbandonedMessages.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_RotatesOwnerTokensAcrossRuns()
    {
        // Arrange
        var handler = new TestInboxHandler("test-topic");
        var resolver = CreateHandlerResolver(handler);
        var store = new StubInboxWorkStore();
        store.AddMessage("msg-stub-3", attempt: 0, topic: "test-topic");
        store.AddMessage("msg-stub-4", attempt: 0, topic: "test-topic");

        var dispatcher = new MultiInboxDispatcher(
            new StubInboxWorkStoreProvider(store),
            new RoundRobinInboxSelectionStrategy(),
            resolver,
            new TestLogger<MultiInboxDispatcher>(TestOutputHelper));

        // Act
        await dispatcher.RunOnceAsync(10, CancellationToken.None);
        await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        store.OwnerTokens.Count.ShouldBe(2);
        store.OwnerTokens.Distinct().Count().ShouldBe(2);
    }

    private SqlInboxService CreateInboxService()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
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
            SchemaName = "infra",
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

    private sealed class StubInboxWorkStore : IInboxWorkStore
    {
        private readonly Dictionary<string, (int Attempt, string Topic)> messages = new(StringComparer.Ordinal);
        private readonly Queue<string> claimQueue = new();

        public List<string> FailedMessages { get; } = new();

        public List<(IEnumerable<string> MessageIds, string? Error, TimeSpan? Delay)> AbandonedMessages { get; } = new();

        public List<Guid> OwnerTokens { get; } = new();

        public void AddMessage(string id, int attempt, string topic)
        {
            messages[id] = (attempt, topic);
            claimQueue.Enqueue(id);
        }

        public Task AckAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken)
        {
            foreach (var id in messageIds)
            {
                messages.Remove(id);
            }

            return Task.CompletedTask;
        }

        public Task AbandonAsync(
            OwnerToken ownerToken,
            IEnumerable<string> messageIds,
            string? lastError = null,
            TimeSpan? delay = null,
            CancellationToken cancellationToken = default)
        {
            AbandonedMessages.Add((messageIds.ToList(), lastError, delay));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
        {
            OwnerTokens.Add(ownerToken.Value);
            var claimed = new List<string>();
            while (claimed.Count < batchSize && claimQueue.Count > 0)
            {
                claimed.Add(claimQueue.Dequeue());
            }

            return Task.FromResult<IReadOnlyList<string>>(claimed);
        }

        public Task FailAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string error, CancellationToken cancellationToken)
        {
            foreach (var id in messageIds)
            {
                FailedMessages.Add(id);
                messages.Remove(id);
            }

            return Task.CompletedTask;
        }

        public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
        {
            var (attempt, topic) = messages[messageId];
            return Task.FromResult(new InboxMessage
            {
                MessageId = messageId,
                Topic = topic,
                Attempt = attempt,
                Payload = string.Empty,
                Source = string.Empty,
            });
        }

        public Task ReapExpiredAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubInboxWorkStoreProvider : IInboxWorkStoreProvider
    {
        private readonly IInboxWorkStore store;

        public StubInboxWorkStoreProvider(IInboxWorkStore store)
        {
            this.store = store;
        }

        public Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync() => Task.FromResult<IReadOnlyList<IInboxWorkStore>>(new[] { store });

        public string GetStoreIdentifier(IInboxWorkStore inboxWorkStore) => "stub";

        public IInboxWorkStore? GetStoreByKey(string key) => store;

        public IInbox? GetInboxByKey(string key) => null;
    }
}
