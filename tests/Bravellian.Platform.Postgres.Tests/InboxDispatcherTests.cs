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

#pragma warning disable CA1822
namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxDispatcherTests : PostgresTestBase
{
    private string qualifiedInboxTableName = string.Empty;
    public InboxDispatcherTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure inbox work queue schema is set up (stored procedures and types)
        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);
        qualifiedInboxTableName = PostgresSqlHelper.Qualify("infra", "Inbox");
    }

    /// <summary>
    /// Given no inbox messages, then RunOnceAsync returns 0 processed items.
    /// </summary>
    /// <intent>
    /// Verify the dispatcher reports zero when no work is available.
    /// </intent>
    /// <scenario>
    /// Given an inbox work store and resolver with no enqueued messages.
    /// </scenario>
    /// <behavior>
    /// The processed count is 0.
    /// </behavior>
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

    /// <summary>
    /// When a queued message has a matching handler, then RunOnceAsync processes it and marks it Done.
    /// </summary>
    /// <intent>
    /// Verify successful dispatch updates the inbox status to Done.
    /// </intent>
    /// <scenario>
    /// Given an enqueued test-topic message and a resolver with a matching handler.
    /// </scenario>
    /// <behavior>
    /// The processed count is 1 and the database status is Done for msg-1.
    /// </behavior>
    [Fact]
    public async Task RunOnceAsync_WithValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var resolver = CreateHandlerResolver(new TestInboxHandler("test-topic"));
        var dispatcher = CreateDispatcher(store, resolver);

        // Enqueue a test message
        var messageId = InboxMessageIdentifier.From("msg-1");
        await inbox.EnqueueAsync("test-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Done
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            $"SELECT \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Done", status);
    }

    /// <summary>
    /// When no handler exists for a message topic, then RunOnceAsync marks the message Dead.
    /// </summary>
    /// <intent>
    /// Verify missing handlers move the message to Dead.
    /// </intent>
    /// <scenario>
    /// Given an enqueued unknown-topic message and a resolver with no handlers.
    /// </scenario>
    /// <behavior>
    /// The processed count is 1 and the database status is Dead for msg-2.
    /// </behavior>
    [Fact]
    public async Task RunOnceAsync_WithNoHandlerForTopic_MarksMessageAsDead()
    {
        // Arrange
        var inbox = CreateInboxService();
        var store = CreateInboxWorkStore();
        var resolver = CreateHandlerResolver(); // No handlers registered
        var dispatcher = CreateDispatcher(store, resolver);

        // Enqueue a test message with unknown topic
        var messageId = InboxMessageIdentifier.From("msg-2");
        await inbox.EnqueueAsync("unknown-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was marked as Dead due to no handler
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            $"SELECT \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Dead", status);
    }

    /// <summary>
    /// When a handler throws, then RunOnceAsync abandons the message back to Seen.
    /// </summary>
    /// <intent>
    /// Verify handler failures result in a retryable abandon.
    /// </intent>
    /// <scenario>
    /// Given an enqueued failing-topic message and a handler that throws.
    /// </scenario>
    /// <behavior>
    /// The processed count is 1 and the message status returns to Seen.
    /// </behavior>
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
        var messageId = InboxMessageIdentifier.From("msg-3");
        await inbox.EnqueueAsync("failing-topic", "test-source", messageId, "test payload", cancellationToken: TestContext.Current.CancellationToken);

        // Act - First attempt should fail and abandon
        var processedCount = await dispatcher.RunOnceAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, processedCount);

        // Verify message was abandoned (back to Seen status)
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            $"SELECT \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Seen", status);
    }

    /// <summary>
    /// When a handler fails and a backoff policy is configured, then the message is abandoned with that delay.
    /// </summary>
    /// <intent>
    /// Verify backoff delays are applied during abandon.
    /// </intent>
    /// <scenario>
    /// Given a stub work store, a failing handler, and a backoff policy returning five seconds.
    /// </scenario>
    /// <behavior>
    /// The message is abandoned once with the configured delay and no failures are recorded.
    /// </behavior>
    [Fact]
    public async Task RunOnceAsync_WithFailingHandler_AbandonsWithBackoffPolicy()
    {
        // Arrange
        var backoffDelay = TimeSpan.FromSeconds(5);
        var failingHandler = new FailingInboxHandler("failing-topic", shouldFail: true);
        var resolver = CreateHandlerResolver(failingHandler);
        var store = new StubInboxWorkStore();
        store.AddMessage(InboxMessageIdentifier.From("msg-stub-1"), attempt: 0, topic: "failing-topic");

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

    /// <summary>
    /// When a message exceeds max attempts, then RunOnceAsync fails it instead of retrying.
    /// </summary>
    /// <intent>
    /// Verify poison messages are failed once they exceed the retry limit.
    /// </intent>
    /// <scenario>
    /// Given a failing handler, a stub message at attempt 5, and maxAttempts set to 5.
    /// </scenario>
    /// <behavior>
    /// The message is recorded as failed and no abandon entries are created.
    /// </behavior>
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
        store.AddMessage(InboxMessageIdentifier.From("msg-stub-2"), attempt: 5, topic: "failing-topic");

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
        store.FailedMessages.ShouldContain(InboxMessageIdentifier.From("msg-stub-2"));
        store.AbandonedMessages.ShouldBeEmpty();
    }

    /// <summary>
    /// When RunOnceAsync executes across multiple runs, then owner tokens rotate between runs.
    /// </summary>
    /// <intent>
    /// Verify each dispatch run uses a new owner token.
    /// </intent>
    /// <scenario>
    /// Given a stub store with two messages and two sequential dispatcher runs.
    /// </scenario>
    /// <behavior>
    /// Two distinct owner tokens are recorded across the runs.
    /// </behavior>
    [Fact]
    public async Task RunOnceAsync_RotatesOwnerTokensAcrossRuns()
    {
        // Arrange
        var handler = new TestInboxHandler("test-topic");
        var resolver = CreateHandlerResolver(handler);
        var store = new StubInboxWorkStore();
        store.AddMessage(InboxMessageIdentifier.From("msg-stub-3"), attempt: 0, topic: "test-topic");
        store.AddMessage(InboxMessageIdentifier.From("msg-stub-4"), attempt: 0, topic: "test-topic");

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

    private PostgresInboxService CreateInboxService()
    {
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
        return new PostgresInboxService(options, logger);
    }

    private PostgresInboxWorkStore CreateInboxWorkStore()
    {
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxWorkStore>(TestOutputHelper);
        return new PostgresInboxWorkStore(options, TimeProvider.System, logger);
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
        private readonly Dictionary<InboxMessageIdentifier, (int Attempt, string Topic)> messages = new();
        private readonly Queue<InboxMessageIdentifier> claimQueue = new();

        public List<InboxMessageIdentifier> FailedMessages { get; } = new();

        public List<(IEnumerable<InboxMessageIdentifier> MessageIds, string? Error, TimeSpan? Delay)> AbandonedMessages { get; } = new();

        public List<Guid> OwnerTokens { get; } = new();

        public void AddMessage(InboxMessageIdentifier id, int attempt, string topic)
        {
            messages[id] = (attempt, topic);
            claimQueue.Enqueue(id);
        }

        public Task AckAsync(OwnerToken ownerToken, IEnumerable<InboxMessageIdentifier> messageIds, CancellationToken cancellationToken)
        {
            foreach (var id in messageIds)
            {
                messages.Remove(id);
            }

            return Task.CompletedTask;
        }

        public Task AbandonAsync(
            OwnerToken ownerToken,
            IEnumerable<InboxMessageIdentifier> messageIds,
            string? lastError = null,
            TimeSpan? delay = null,
            CancellationToken cancellationToken = default)
        {
            AbandonedMessages.Add((messageIds.ToList(), lastError, delay));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InboxMessageIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
        {
            OwnerTokens.Add(ownerToken.Value);
            var claimed = new List<InboxMessageIdentifier>();
            while (claimed.Count < batchSize && claimQueue.Count > 0)
            {
                claimed.Add(claimQueue.Dequeue());
            }

            return Task.FromResult<IReadOnlyList<InboxMessageIdentifier>>(claimed);
        }

        public Task FailAsync(OwnerToken ownerToken, IEnumerable<InboxMessageIdentifier> messageIds, string error, CancellationToken cancellationToken)
        {
            foreach (var id in messageIds)
            {
                FailedMessages.Add(id);
                messages.Remove(id);
            }

            return Task.CompletedTask;
        }

        public Task ReviveAsync(IEnumerable<InboxMessageIdentifier> messageIds, string? reason = null, TimeSpan? delay = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<InboxMessage> GetAsync(InboxMessageIdentifier messageId, CancellationToken cancellationToken)
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
#pragma warning restore CA1822


