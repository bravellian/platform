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
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Concurrent;

public class MultiInboxDispatcherTests : SqlServerTestBase
{
    private readonly ConcurrentBag<string> processedMessages = new();

    public MultiInboxDispatcherTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task MultiInboxDispatcher_ProcessesMessagesFromMultipleStores()
    {
        // Arrange - Create two separate inbox stores with different schemas
        var schema1 = "dbo";
        var schema2 = "tenant1";

        // Create schema2 if it doesn't exist
        await using var setupConnection = new SqlConnection(this.ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync($"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema2}') EXEC('CREATE SCHEMA [{schema2}]')");

        // Create inbox tables in both schemas
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.ConnectionString, schema1, "Inbox");
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.ConnectionString, schema2, "Inbox");

        // Insert test messages into both inboxes
        var message1Id = Guid.NewGuid().ToString();
        var message2Id = Guid.NewGuid().ToString();

        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"INSERT INTO [{schema1}].[Inbox] 
               (MessageId, Source, Topic, Payload, Hash, Status, Attempts, FirstSeenUtc, LastSeenUtc)
               VALUES (@MessageId, @Source, @Topic, @Payload, NULL, 'Seen', 0, @FirstSeenUtc, @LastSeenUtc)",
            new
            {
                MessageId = message1Id,
                Source = "TestSource",
                Topic = "Test.Topic",
                Payload = "message from schema1",
                FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            });

        await connection.ExecuteAsync(
            $@"INSERT INTO [{schema2}].[Inbox] 
               (MessageId, Source, Topic, Payload, Hash, Status, Attempts, FirstSeenUtc, LastSeenUtc)
               VALUES (@MessageId, @Source, @Topic, @Payload, NULL, 'Seen', 0, @FirstSeenUtc, @LastSeenUtc)",
            new
            {
                MessageId = message2Id,
                Source = "TestSource",
                Topic = "Test.Topic",
                Payload = "message from schema2",
                FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            });

        // Create inbox work stores
        var storeLogger = new TestLogger<SqlInboxWorkStore>(this.TestOutputHelper);

        var store1 = new SqlInboxWorkStore(
            Microsoft.Extensions.Options.Options.Create(new SqlInboxOptions
            {
                ConnectionString = this.ConnectionString,
                SchemaName = schema1,
                TableName = "Inbox",
            }),
            storeLogger);

        var store2 = new SqlInboxWorkStore(
            Microsoft.Extensions.Options.Options.Create(new SqlInboxOptions
            {
                ConnectionString = this.ConnectionString,
                SchemaName = schema2,
                TableName = "Inbox",
            }),
            storeLogger);

        // Create store provider
        var storeProvider = new TestInboxWorkStoreProvider(new[] { store1, store2 });

        // Create selection strategy
        var strategy = new RoundRobinInboxSelectionStrategy();

        // Create handler
        var handler = new TestInboxHandler("Test.Topic", this.processedMessages);
        var resolver = new TestInboxHandlerResolver(new[] { handler });

        // Create dispatcher
        var dispatcherLogger = new TestLogger<MultiInboxDispatcher>(this.TestOutputHelper);
        var dispatcher = new MultiInboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

        // Act - Run the dispatcher twice to process both messages
        var count1 = await dispatcher.RunOnceAsync(10, CancellationToken.None);
        var count2 = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        count1.ShouldBe(1);
        count2.ShouldBe(1);
        this.processedMessages.Count.ShouldBe(2);
        this.processedMessages.ShouldContain("message from schema1");
        this.processedMessages.ShouldContain("message from schema2");

        // Verify messages are marked as done in both databases
        var status1 = await connection.QueryFirstAsync<string>(
            $"SELECT Status FROM [{schema1}].[Inbox] WHERE MessageId = @MessageId",
            new { MessageId = message1Id });
        status1.ShouldBe("Done");

        var status2 = await connection.QueryFirstAsync<string>(
            $"SELECT Status FROM [{schema2}].[Inbox] WHERE MessageId = @MessageId",
            new { MessageId = message2Id });
        status2.ShouldBe("Done");
    }

    [Fact]
    public async Task MultiInboxDispatcher_WithDrainFirstStrategy_DrainsOneStoreBeforeMoving()
    {
        // Arrange - Create one store with multiple messages
        var schema1 = "dbo";

        await DatabaseSchemaManager.EnsureInboxSchemaAsync(this.ConnectionString, schema1, "Inbox");

        // Insert 3 messages into the first inbox
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 3; i++)
        {
            await connection.ExecuteAsync(
                $@"INSERT INTO [{schema1}].[Inbox] 
                   (MessageId, Source, Topic, Payload, Hash, Status, Attempts, FirstSeenUtc, LastSeenUtc)
                   VALUES (@MessageId, @Source, @Topic, @Payload, NULL, 'Seen', 0, @FirstSeenUtc, @LastSeenUtc)",
                new
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Source = "TestSource",
                    Topic = "Test.Topic",
                    Payload = $"message {i} from schema1",
                    FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                });
        }

        // Create inbox work stores
        var storeLogger = new TestLogger<SqlInboxWorkStore>(this.TestOutputHelper);

        var store1 = new SqlInboxWorkStore(
            Microsoft.Extensions.Options.Options.Create(new SqlInboxOptions
            {
                ConnectionString = this.ConnectionString,
                SchemaName = schema1,
                TableName = "Inbox",
            }),
            storeLogger);

        var store2 = new SqlInboxWorkStore(
            Microsoft.Extensions.Options.Options.Create(new SqlInboxOptions
            {
                ConnectionString = this.ConnectionString,
                SchemaName = schema1,
                TableName = "Inbox",
            }),
            storeLogger);

        // Create store provider
        var storeProvider = new TestInboxWorkStoreProvider(new[] { store1, store2 });

        // Create drain-first strategy
        var strategy = new DrainFirstInboxSelectionStrategy();

        // Create handler
        var handler = new TestInboxHandler("Test.Topic", this.processedMessages);
        var resolver = new TestInboxHandlerResolver(new[] { handler });

        // Create dispatcher
        var dispatcherLogger = new TestLogger<MultiInboxDispatcher>(this.TestOutputHelper);
        var dispatcher = new MultiInboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

        // Act - Process with batch size of 1 to ensure we drain one store first
        var count1 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count2 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count3 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count4 = await dispatcher.RunOnceAsync(1, CancellationToken.None); // Should be 0

        // Assert
        count1.ShouldBe(1);
        count2.ShouldBe(1);
        count3.ShouldBe(1);
        count4.ShouldBe(0); // No more messages
        this.processedMessages.Count.ShouldBe(3);
    }

    private class TestInboxWorkStoreProvider : IInboxWorkStoreProvider
    {
        private readonly IReadOnlyList<IInboxWorkStore> stores;

        public TestInboxWorkStoreProvider(IEnumerable<IInboxWorkStore> stores)
        {
            this.stores = stores.ToList();
        }

        public IReadOnlyList<IInboxWorkStore> GetAllStores() => this.stores;

        public string GetStoreIdentifier(IInboxWorkStore store)
        {
            for (int i = 0; i < this.stores.Count; i++)
            {
                if (ReferenceEquals(this.stores[i], store))
                {
                    return $"Store{i + 1}";
                }
            }

            return "Unknown";
        }
    }

    private class TestInboxHandler : IInboxHandler
    {
        private readonly ConcurrentBag<string> processedMessages;

        public TestInboxHandler(string topic, ConcurrentBag<string> processedMessages)
        {
            this.Topic = topic;
            this.processedMessages = processedMessages;
        }

        public string Topic { get; }

        public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
        {
            this.processedMessages.Add(message.Payload);
            return Task.CompletedTask;
        }
    }

    private class TestInboxHandlerResolver : IInboxHandlerResolver
    {
        private readonly Dictionary<string, IInboxHandler> handlers;

        public TestInboxHandlerResolver(IEnumerable<IInboxHandler> handlers)
        {
            this.handlers = handlers.ToDictionary(h => h.Topic);
        }

        public IInboxHandler GetHandler(string topic)
        {
            if (this.handlers.TryGetValue(topic, out var handler))
            {
                return handler;
            }

            throw new InvalidOperationException($"No handler found for topic '{topic}'");
        }
    }
}
