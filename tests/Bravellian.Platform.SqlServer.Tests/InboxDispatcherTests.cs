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

    /// <summary>When RunOnceAsync is called with no inbox messages, then it returns zero processed items.</summary>
    /// <intent>Verify the dispatcher reports no work when the store is empty.</intent>
    /// <scenario>Given a SqlInboxWorkStore with no queued messages and an empty handler resolver.</scenario>
    /// <behavior>Then RunOnceAsync returns 0.</behavior>
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

    /// <summary>When a message has a matching handler, then RunOnceAsync processes it and marks it Done.</summary>
    /// <intent>Validate successful handler execution updates inbox status.</intent>
    /// <scenario>Given an enqueued inbox message and a TestInboxHandler registered for its topic.</scenario>
    /// <behavior>Then RunOnceAsync returns 1 and the message status is Done.</behavior>
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
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Done", status);
    }

    /// <summary>When no handler exists for a message topic, then RunOnceAsync marks the message Dead.</summary>
    /// <intent>Ensure unhandled topics are failed instead of retried indefinitely.</intent>
    /// <scenario>Given an enqueued inbox message with an unknown topic and no registered handlers.</scenario>
    /// <behavior>Then RunOnceAsync returns 1 and the message status is Dead.</behavior>
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
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Dead", status);
    }

    /// <summary>When a handler throws, then RunOnceAsync abandons the message for retry.</summary>
    /// <intent>Validate failure paths return messages to Seen for future retries.</intent>
    /// <scenario>Given an enqueued message handled by a FailingInboxHandler that throws.</scenario>
    /// <behavior>Then RunOnceAsync returns 1 and the message status is Seen.</behavior>
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
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId.Value });

        Assert.Equal("Seen", status);
    }

    /// <summary>When a handler fails and a backoff policy is supplied, then the message is abandoned with that delay.</summary>
    /// <intent>Ensure dispatcher honors custom backoff delays on failure.</intent>
    /// <scenario>Given a StubInboxWorkStore, a failing handler, and a backoff policy returning 5 seconds.</scenario>
    /// <behavior>Then the message is abandoned with the configured delay and not failed.</behavior>
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

    /// <summary>When a message exceeds max attempts, then RunOnceAsync fails it instead of retrying.</summary>
    /// <intent>Verify poison-message handling respects maxAttempts.</intent>
    /// <scenario>Given a StubInboxWorkStore message with attempt count at maxAttempts.</scenario>
    /// <behavior>Then the message is marked failed and not abandoned.</behavior>
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

    /// <summary>When RunOnceAsync is called multiple times, then each run uses a new owner token.</summary>
    /// <intent>Ensure dispatcher rotates owner tokens across separate runs.</intent>
    /// <scenario>Given a StubInboxWorkStore and two RunOnceAsync calls.</scenario>
    /// <behavior>Then the store captures two distinct owner tokens.</behavior>
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

