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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MultiOutboxDispatcherTests : PostgresTestBase
{
    private readonly FakeTimeProvider timeProvider = new();
    private readonly ConcurrentBag<string> processedMessages = new();

    public MultiOutboxDispatcherTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    [Fact]
    public async Task MultiOutboxDispatcher_ProcessesMessagesFromMultipleStores()
    {
        var schema1 = "infra";
        var schema2 = "tenant1";

        await using var setupConnection = new NpgsqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);
        await setupConnection.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS {PostgresSqlHelper.QuoteIdentifier(schema2)}");

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema1, "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema2, "Outbox").ConfigureAwait(false);

        var message1Id = Guid.NewGuid();
        var message2Id = Guid.NewGuid();

        var table1 = PostgresSqlHelper.Qualify(schema1, "Outbox");
        var table2 = PostgresSqlHelper.Qualify(schema2, "Outbox");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {table1}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount", "MessageId")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @MessageId)
            """,
            new
            {
                Id = message1Id,
                Topic = "Test.Topic",
                Payload = "message from schema1",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                MessageId = Guid.NewGuid(),
            });

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {table2}
            ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount", "MessageId")
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @MessageId)
            """,
            new
            {
                Id = message2Id,
                Topic = "Test.Topic",
                Payload = "message from schema2",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                MessageId = Guid.NewGuid(),
            });

        var storeLogger = new TestLogger<PostgresOutboxStore>(TestOutputHelper);

        var store1 = new PostgresOutboxStore(
            Options.Create(new PostgresOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var store2 = new PostgresOutboxStore(
            Options.Create(new PostgresOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema2,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var storeProvider = new TestOutboxStoreProvider(new[] { store1, store2 });
        var strategy = new RoundRobinOutboxSelectionStrategy();

        var handler = new TestOutboxHandler("Test.Topic", processedMessages);
        var resolver = new OutboxHandlerResolver(new[] { handler });

        var dispatcherLogger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = new MultiOutboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

        var count1 = await dispatcher.RunOnceAsync(10, CancellationToken.None);
        var count2 = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        count1.ShouldBe(1);
        count2.ShouldBe(1);
        processedMessages.Count.ShouldBe(2);
        processedMessages.ShouldContain("message from schema1");
        processedMessages.ShouldContain("message from schema2");

        var processed1 = await connection.QueryFirstAsync<bool>(
            $"""
            SELECT "IsProcessed"
            FROM {table1}
            WHERE "Id" = @Id
            """,
            new { Id = message1Id });
        processed1.ShouldBeTrue();

        var processed2 = await connection.QueryFirstAsync<bool>(
            $"""
            SELECT "IsProcessed"
            FROM {table2}
            WHERE "Id" = @Id
            """,
            new { Id = message2Id });
        processed2.ShouldBeTrue();
    }

    [Fact]
    public async Task MultiOutboxDispatcher_WithDrainFirstStrategy_DrainsOneStoreBeforeMoving()
    {
        var schema1 = "infra";

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, schema1, "Outbox").ConfigureAwait(false);

        var table = PostgresSqlHelper.Qualify(schema1, "Outbox");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (int i = 0; i < 3; i++)
        {
            await connection.ExecuteAsync(
                $"""
                INSERT INTO {table}
                ("Id", "Topic", "Payload", "Status", "CreatedAt", "RetryCount", "MessageId")
                VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @MessageId)
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    Topic = "Test.Topic",
                    Payload = $"message {i} from schema1",
                    Status = OutboxStatus.Ready,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    MessageId = Guid.NewGuid(),
                });
        }

        var storeLogger = new TestLogger<PostgresOutboxStore>(TestOutputHelper);

        var store1 = new PostgresOutboxStore(
            Options.Create(new PostgresOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var store2 = new PostgresOutboxStore(
            Options.Create(new PostgresOutboxOptions
            {
                ConnectionString = ConnectionString,
                SchemaName = schema1,
                TableName = "Outbox",
            }),
            timeProvider,
            storeLogger);

        var storeProvider = new TestOutboxStoreProvider(new[] { store1, store2 });
        var strategy = new DrainFirstOutboxSelectionStrategy();

        var handler = new TestOutboxHandler("Test.Topic", processedMessages);
        var resolver = new OutboxHandlerResolver(new[] { handler });

        var dispatcherLogger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = new MultiOutboxDispatcher(storeProvider, strategy, resolver, dispatcherLogger);

        var count1 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count2 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count3 = await dispatcher.RunOnceAsync(1, CancellationToken.None);
        var count4 = await dispatcher.RunOnceAsync(1, CancellationToken.None);

        count1.ShouldBe(1);
        count2.ShouldBe(1);
        count3.ShouldBe(1);
        count4.ShouldBe(0);
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
