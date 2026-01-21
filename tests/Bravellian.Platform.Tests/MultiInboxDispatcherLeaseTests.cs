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


using System.Collections.Concurrent;
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MultiInboxDispatcherLeaseTests : SqlServerTestBase
{
    private readonly FakeTimeProvider timeProvider = new();
    private readonly ConcurrentBag<string> processedMessages = new();

    public MultiInboxDispatcherLeaseTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    [Fact]
    public async Task MultiInboxDispatcher_WithLease_PreventsConcurrentProcessing()
    {
        // Arrange - Create inbox and distributed lock tables
        var schema = "infra";
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString, schema, "Inbox");
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(ConnectionString, schema);

        // Insert test messages
        var messageIds = new List<string>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 5; i++)
        {
            var messageId = $"msg-{i}";
            messageIds.Add(messageId);
            await connection.ExecuteAsync(
                $@"INSERT INTO [{schema}].[Inbox] 
                   (MessageId, Source, Topic, Payload, FirstSeenUtc, LastSeenUtc, Attempts, Status)
                   VALUES (@MessageId, @Source, @Topic, @Payload, @FirstSeenUtc, @LastSeenUtc, 0, 'Seen')",
                new
                {
                    MessageId = messageId,
                    Source = "test",
                    Topic = "Test.Topic",
                    Payload = $"message {i}",
                    FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                });
        }

        // Create inbox work store
        var storeLogger = new TestLogger<SqlInboxWorkStore>(TestOutputHelper);
        var store = new SqlInboxWorkStore(
            Options.Create(new SqlInboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema,
                TableName = "Inbox",
            }),
            timeProvider,
            storeLogger);

        // Create store provider
        var storeProvider = new TestInboxWorkStoreProvider(new[] { store });

        // Create selection strategy
        var strategy = new RoundRobinInboxSelectionStrategy();

        // Create handler
        var handler = new TestInboxHandler("Test.Topic", processedMessages);
        var resolver = new InboxHandlerResolver(new[] { handler });

        // Create lease factory
        var leaseFactory = new SqlLeaseFactory(
            new LeaseFactoryConfig
            {
                ConnectionString = ConnectionString,
                SchemaName = schema,
            },
            new TestLogger<SqlLeaseFactory>(TestOutputHelper));

        // Create lease router
        var leaseRouter = new TestLeaseRouter(leaseFactory);

        // Create two dispatchers (simulating two workers on different machines)
        var dispatcherLogger1 = new TestLogger<MultiInboxDispatcher>(TestOutputHelper);
        var dispatcher1 = new MultiInboxDispatcher(
            storeProvider,
            strategy,
            resolver,
            dispatcherLogger1,
            leaseRouter,
            leaseDuration: TimeSpan.FromSeconds(5));

        var dispatcherLogger2 = new TestLogger<MultiInboxDispatcher>(TestOutputHelper);
        var dispatcher2 = new MultiInboxDispatcher(
            storeProvider,
            strategy,
            resolver,
            dispatcherLogger2,
            leaseRouter,
            leaseDuration: TimeSpan.FromSeconds(5));

        // Act - Run both dispatchers concurrently
        var task1 = Task.Run(async () => await dispatcher1.RunOnceAsync(10, CancellationToken.None).ConfigureAwait(false));
        var task2 = Task.Run(async () => await dispatcher2.RunOnceAsync(10, CancellationToken.None).ConfigureAwait(false));

        var results = await Task.WhenAll(task1, task2).ConfigureAwait(true);

        // Assert - Only one dispatcher should have processed messages
        // The other should have been blocked by the lease
        var dispatcher1Count = results[0];
        var dispatcher2Count = results[1];

        TestOutputHelper.WriteLine($"Dispatcher 1 processed: {dispatcher1Count}");
        TestOutputHelper.WriteLine($"Dispatcher 2 processed: {dispatcher2Count}");

        // One should process all messages, the other should process none
        (dispatcher1Count == 5 || dispatcher2Count == 5).ShouldBeTrue("One dispatcher should have processed all 5 messages");
        (dispatcher1Count == 0 || dispatcher2Count == 0).ShouldBeTrue("The other dispatcher should have been blocked by the lease");

        // Total processed should be 5
        (dispatcher1Count + dispatcher2Count).ShouldBe(5);
        processedMessages.Count.ShouldBe(5);
    }

    [Fact]
    public async Task MultiInboxDispatcher_WithoutLease_AllowsProcessing()
    {
        // Arrange - Create inbox table (no lease table)
        var schema = "infra";
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString, schema, "Inbox");

        // Insert test messages
        var messageIds = new List<string>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 3; i++)
        {
            var messageId = $"msg-{i}";
            messageIds.Add(messageId);
            await connection.ExecuteAsync(
                $@"INSERT INTO [{schema}].[Inbox] 
                   (MessageId, Source, Topic, Payload, FirstSeenUtc, LastSeenUtc, Attempts, Status)
                   VALUES (@MessageId, @Source, @Topic, @Payload, @FirstSeenUtc, @LastSeenUtc, 0, 'Seen')",
                new
                {
                    MessageId = messageId,
                    Source = "test",
                    Topic = "Test.Topic",
                    Payload = $"message {i}",
                    FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                });
        }

        // Create inbox work store
        var storeLogger = new TestLogger<SqlInboxWorkStore>(TestOutputHelper);
        var store = new SqlInboxWorkStore(
            Options.Create(new SqlInboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema,
                TableName = "Inbox",
            }),
            timeProvider,
            storeLogger);

        // Create store provider
        var storeProvider = new TestInboxWorkStoreProvider(new[] { store });

        // Create selection strategy
        var strategy = new RoundRobinInboxSelectionStrategy();

        // Create handler
        var handler = new TestInboxHandler("Test.Topic", processedMessages);
        var resolver = new InboxHandlerResolver(new[] { handler });

        // Create dispatcher WITHOUT lease router (null)
        var dispatcherLogger = new TestLogger<MultiInboxDispatcher>(TestOutputHelper);
        var dispatcher = new MultiInboxDispatcher(
            storeProvider,
            strategy,
            resolver,
            dispatcherLogger,
            leaseRouter: null); // No lease router

        // Act - Run the dispatcher
        var count = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert - Should process all messages without lease
        count.ShouldBe(3);
        processedMessages.Count.ShouldBe(3);
    }

    // Helper class to provide a simple test lease router
    private class TestLeaseRouter : ILeaseRouter
    {
        private readonly ISystemLeaseFactory factory;

        public TestLeaseRouter(ISystemLeaseFactory factory)
        {
            this.factory = factory;
        }

        public Task<ISystemLeaseFactory> GetLeaseFactoryAsync(string routingKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(factory);
        }

        public Task<ISystemLeaseFactory> GetDefaultLeaseFactoryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(factory);
        }
    }

    // Helper class for test store provider
    private class TestInboxWorkStoreProvider : IInboxWorkStoreProvider
    {
        private readonly IReadOnlyList<IInboxWorkStore> stores;

        public TestInboxWorkStoreProvider(IEnumerable<IInboxWorkStore> stores)
        {
            this.stores = stores.ToList();
        }

        public Task<IReadOnlyList<IInboxWorkStore>> GetAllStoresAsync() => Task.FromResult(stores);

        public string GetStoreIdentifier(IInboxWorkStore store) => "test-tenant";

        public IInboxWorkStore? GetStoreByKey(string key) => stores.FirstOrDefault();

        public IInbox? GetInboxByKey(string key) => null;
    }

    // Helper class for test inbox handler
    private class TestInboxHandler : IInboxHandler
    {
        private readonly string topic;
        private readonly ConcurrentBag<string> processedMessages;

        public TestInboxHandler(string topic, ConcurrentBag<string> processedMessages)
        {
            this.topic = topic;
            this.processedMessages = processedMessages;
        }

        public string Topic => topic;

        public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
        {
            processedMessages.Add(message.Payload);
            return Task.CompletedTask;
        }
    }
}
