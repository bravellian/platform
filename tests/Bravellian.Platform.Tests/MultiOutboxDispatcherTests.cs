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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MultiOutboxDispatcherTests : SqlServerTestBase
{
    private readonly FakeTimeProvider timeProvider = new();
    private readonly ConcurrentBag<string> processedMessages = new();

    public MultiOutboxDispatcherTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    [Fact]
    public async Task MultiOutboxDispatcher_ProcessesMessagesFromMultipleStores()
    {
        // Arrange - Create two separate outbox stores with different schemas
        var schema1 = "dbo";
        var schema2 = "tenant1";

        // Create schema2 if it doesn't exist
        await using var setupConnection = new SqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync($"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema2}') EXEC('CREATE SCHEMA [{schema2}]')");

        // Create outbox tables in both schemas
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema1, "Outbox");
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema2, "Outbox");

        // Insert test messages into both outboxes
        var message1Id = Guid.NewGuid();
        var message2Id = Guid.NewGuid();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"INSERT INTO [{schema1}].[Outbox] 
               (Id, Topic, Payload, Status, CreatedAt, RetryCount)
               VALUES (@Id, @Topic, @Payload, 0, @CreatedAt, 0)",
            new
            {
                Id = message1Id,
                Topic = "Test.Topic",
                Payload = "message from schema1",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        await connection.ExecuteAsync(
            $@"INSERT INTO [{schema2}].[Outbox] 
               (Id, Topic, Payload, Status, CreatedAt, RetryCount)
               VALUES (@Id, @Topic, @Payload, 0, @CreatedAt, 0)",
            new
            {
                Id = message2Id,
                Topic = "Test.Topic",
                Payload = "message from schema2",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        // Create outbox stores
        // Create logger
        var storeLogger = new TestLogger<SqlOutboxStore>(TestOutputHelper);

        var store1 = new SqlOutboxStore(
            Options.Create(new SqlOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var store2 = new SqlOutboxStore(
            Options.Create(new SqlOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema2,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        // Create store provider
        var storeProvider = new TestOutboxStoreProvider(new[] { store1, store2 });

        // Create selection strategy
        var strategy = new RoundRobinOutboxSelectionStrategy();

        // Create handler
        var handler = new TestOutboxHandler("Test.Topic", processedMessages);
        var resolver = new OutboxHandlerResolver(new[] { handler });

        // Create dispatcher
        var dispatcherLogger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = new MultiOutboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

        // Act - Run the dispatcher twice to process both messages
        var count1 = await dispatcher.RunOnceAsync(10, CancellationToken.None);
        var count2 = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        count1.ShouldBe(1);
        count2.ShouldBe(1);
        processedMessages.Count.ShouldBe(2);
        processedMessages.ShouldContain("message from schema1");
        processedMessages.ShouldContain("message from schema2");

        // Verify messages are marked as processed in both databases
        var processed1 = await connection.QueryFirstAsync<bool>(
            $"SELECT IsProcessed FROM [{schema1}].[Outbox] WHERE Id = @Id",
            new { Id = message1Id });
        processed1.ShouldBeTrue();

        var processed2 = await connection.QueryFirstAsync<bool>(
            $"SELECT IsProcessed FROM [{schema2}].[Outbox] WHERE Id = @Id",
            new { Id = message2Id });
        processed2.ShouldBeTrue();
    }

    [Fact]
    public async Task MultiOutboxDispatcher_WithDrainFirstStrategy_DrainsOneStoreBeforeMoving()
    {
        // Arrange - Create one store with multiple messages
        var schema1 = "dbo";

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema1, "Outbox");

        // Insert 3 messages into the first outbox
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 3; i++)
        {
            await connection.ExecuteAsync(
                $@"INSERT INTO [{schema1}].[Outbox] 
                   (Id, Topic, Payload, Status, CreatedAt, RetryCount)
                   VALUES (@Id, @Topic, @Payload, 0, @CreatedAt, 0)",
                new
                {
                    Id = Guid.NewGuid(),
                    Topic = "Test.Topic",
                    Payload = $"message {i} from schema1",
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                });
        }

        // Create outbox stores
        // Note: Both stores point to the same table, which is intentional for this test.
        // We're testing that the drain-first strategy keeps processing from the same store
        // until it's empty, not necessarily that they're different physical stores.
        // Create logger
        var storeLogger = new TestLogger<SqlOutboxStore>(TestOutputHelper);

        var store1 = new SqlOutboxStore(
            Options.Create(new SqlOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var store2 = new SqlOutboxStore(
            Options.Create(new SqlOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        // Create store provider
        var storeProvider = new TestOutboxStoreProvider(new[] { store1, store2 });

        // Create drain-first strategy
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Create handler
        var handler = new TestOutboxHandler("Test.Topic", processedMessages);
        var resolver = new OutboxHandlerResolver(new[] { handler });

        // Create dispatcher
        var dispatcherLogger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = new MultiOutboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

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
        processedMessages.Count.ShouldBe(3);
    }

    private class TestOutboxStoreProvider : IOutboxStoreProvider
    {
        private readonly IReadOnlyList<IOutboxStore> stores;

        public TestOutboxStoreProvider(IEnumerable<IOutboxStore> stores)
        {
            this.stores = stores.ToList();
        }

        public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync() => Task.FromResult(stores);

        public string GetStoreIdentifier(IOutboxStore store)
        {
            for (int i = 0; i < stores.Count; i++)
            {
                if (ReferenceEquals(stores[i], store))
                {
                    return $"Store{i + 1}";
                }
            }

            return "Unknown";
        }

        public IOutboxStore? GetStoreByKey(string key)
        {
            // Simple implementation for testing
            for (int i = 0; i < stores.Count; i++)
            {
                if (string.Equals($"Store{i + 1}", key, StringComparison.Ordinal))
                {
                    return stores[i];
                }
            }

            return null;
        }

        public IOutbox? GetOutboxByKey(string key)
        {
            // Not used in these tests
            return null;
        }
    }

    private class TestOutboxHandler : IOutboxHandler
    {
        private readonly ConcurrentBag<string> processedMessages;

        public TestOutboxHandler(string topic, ConcurrentBag<string> processedMessages)
        {
            Topic = topic;
            this.processedMessages = processedMessages;
        }

        public string Topic { get; }

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            processedMessages.Add(message.Payload);
            return Task.CompletedTask;
        }
    }
}
