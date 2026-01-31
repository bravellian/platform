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

using Bravellian.Platform.Outbox;
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxCleanupTests : PostgresTestBase
{
    private readonly PostgresOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox" };
    private string qualifiedTableName = string.Empty;

    public OutboxCleanupTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        qualifiedTableName = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);
    }

    /// <summary>When cleanup runs with a seven-day retention period, then only old processed messages are deleted.</summary>
    /// <intent>Verify cleanup removes processed rows older than the retention window.</intent>
    /// <scenario>Given one processed message older than 7 days, one recent processed message, and one unprocessed message.</scenario>
    /// <behavior>Cleanup deletes only the old processed row and leaves the recent and unprocessed rows intact.</behavior>
    [Fact]
    public async Task Cleanup_DeletesOldProcessedMessages()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var oldMessageId = Guid.NewGuid();
        var recentMessageId = Guid.NewGuid();
        var unprocessedMessageId = Guid.NewGuid();

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "IsProcessed", "ProcessedAt", "CreatedAt", "Status", "MessageId")
            VALUES (@Id, @Topic, @Payload, TRUE, @ProcessedAt, @CreatedAt, @Status, @MessageId)
            """,
            new
            {
                Id = oldMessageId,
                Topic = "Test.Topic",
                Payload = "old message",
                ProcessedAt = DateTimeOffset.UtcNow.AddDays(-10),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-11),
                Status = OutboxStatus.Done,
                MessageId = Guid.NewGuid(),
            });

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "IsProcessed", "ProcessedAt", "CreatedAt", "Status", "MessageId")
            VALUES (@Id, @Topic, @Payload, TRUE, @ProcessedAt, @CreatedAt, @Status, @MessageId)
            """,
            new
            {
                Id = recentMessageId,
                Topic = "Test.Topic",
                Payload = "recent message",
                ProcessedAt = DateTimeOffset.UtcNow.AddDays(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                Status = OutboxStatus.Done,
                MessageId = Guid.NewGuid(),
            });

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "IsProcessed", "CreatedAt", "Status", "MessageId")
            VALUES (@Id, @Topic, @Payload, FALSE, @CreatedAt, @Status, @MessageId)
            """,
            new
            {
                Id = unprocessedMessageId,
                Topic = "Test.Topic",
                Payload = "unprocessed message",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
                Status = OutboxStatus.Ready,
                MessageId = Guid.NewGuid(),
            });

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(7));

        deletedCount.ShouldBe(1);

        var remainingMessages = await connection.QueryAsync<Guid>(
            $"""
            SELECT "Id"
            FROM {qualifiedTableName}
            """);

        var remainingIds = remainingMessages.ToList();
        remainingIds.ShouldNotContain(oldMessageId);
        remainingIds.ShouldContain(recentMessageId);
        remainingIds.ShouldContain(unprocessedMessageId);
    }

    /// <summary>When only recent processed messages exist, then cleanup deletes nothing.</summary>
    /// <intent>Verify cleanup does not remove rows inside the retention window.</intent>
    /// <scenario>Given a processed message with ProcessedAt within the retention period.</scenario>
    /// <behavior>Deleted count is 0 and the row remains.</behavior>
    [Fact]
    public async Task Cleanup_WithNoOldMessages_DeletesNothing()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var recentMessageId = Guid.NewGuid();

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("Id", "Topic", "Payload", "IsProcessed", "ProcessedAt", "CreatedAt", "Status", "MessageId")
            VALUES (@Id, @Topic, @Payload, TRUE, @ProcessedAt, @CreatedAt, @Status, @MessageId)
            """,
            new
            {
                Id = recentMessageId,
                Topic = "Test.Topic",
                Payload = "recent message",
                ProcessedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                Status = OutboxStatus.Done,
                MessageId = Guid.NewGuid(),
            });

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(7));

        deletedCount.ShouldBe(0);

        var remainingMessages = await connection.QueryAsync<Guid>(
            $"""
            SELECT "Id"
            FROM {qualifiedTableName}
            """);

        remainingMessages.Count().ShouldBe(1);
    }

    /// <summary>When cleanup uses a 10-day retention period, then only messages older than 10 days are deleted.</summary>
    /// <intent>Verify retention cutoffs are respected during cleanup.</intent>
    /// <scenario>Given processed messages at 30, 15, 7, 3, and 1 days old.</scenario>
    /// <behavior>Two oldest rows are deleted and the three newest rows remain.</behavior>
    [Fact]
    public async Task Cleanup_RespectsRetentionPeriod()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var messages = new[]
        {
            (Id: Guid.NewGuid(), DaysAgo: 30),
            (Id: Guid.NewGuid(), DaysAgo: 15),
            (Id: Guid.NewGuid(), DaysAgo: 7),
            (Id: Guid.NewGuid(), DaysAgo: 3),
            (Id: Guid.NewGuid(), DaysAgo: 1),
        };

        foreach (var (id, daysAgo) in messages)
        {
            await connection.ExecuteAsync(
                $"""
                INSERT INTO {qualifiedTableName}
                ("Id", "Topic", "Payload", "IsProcessed", "ProcessedAt", "CreatedAt", "Status", "MessageId")
                VALUES (@Id, @Topic, @Payload, TRUE, @ProcessedAt, @CreatedAt, @Status, @MessageId)
                """,
                new
                {
                    Id = id,
                    Topic = "Test.Topic",
                    Payload = $"message {daysAgo} days old",
                    ProcessedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo - 1),
                    Status = OutboxStatus.Done,
                    MessageId = Guid.NewGuid(),
                });
        }

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(10));

        deletedCount.ShouldBe(2);

        var remainingMessages = await connection.QueryAsync<Guid>(
            $"""
            SELECT "Id"
            FROM {qualifiedTableName}
            """);

        var remainingIds = remainingMessages.ToList();
        remainingIds.Count.ShouldBe(3);
        remainingIds.ShouldNotContain(messages[0].Id);
        remainingIds.ShouldNotContain(messages[1].Id);
        remainingIds.ShouldContain(messages[2].Id);
        remainingIds.ShouldContain(messages[3].Id);
        remainingIds.ShouldContain(messages[4].Id);
    }

    /// <summary>When the outbox table is missing, then the cleanup service runs without recreating it.</summary>
    /// <intent>Verify cleanup handles missing tables without side effects.</intent>
    /// <scenario>Given the outbox table is dropped and the cleanup service runs with a short interval.</scenario>
    /// <behavior>The service completes and the outbox table still does not exist.</behavior>
    [Fact]
    public async Task CleanupService_GracefullyHandles_MissingTable()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteAsync($"DROP TABLE IF EXISTS {qualifiedTableName}");

        var mono = new FakeMonotonicClock();
        var logger = new TestLogger<OutboxCleanupService>(TestOutputHelper);

        var options = new PostgresOutboxOptions
        {
            ConnectionString = defaultOptions.ConnectionString,
            SchemaName = defaultOptions.SchemaName,
            TableName = defaultOptions.TableName,
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromMilliseconds(100),
        };

        using var service = new OutboxCleanupService(
            Options.Create(options),
            mono,
            logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var startTask = service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await service.StopAsync(CancellationToken.None);
        await startTask;

        var tableExists = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @SchemaName AND table_name = @TableName
            """,
            new { SchemaName = defaultOptions.SchemaName, TableName = defaultOptions.TableName });

        tableExists.ShouldBe(0);
    }

    private async Task<int> ExecuteCleanupAsync(TimeSpan retentionPeriod)
    {
        var sql = $"""
            WITH deleted AS (
                DELETE FROM {qualifiedTableName}
                WHERE "Status" = 2
                    AND "ProcessedAt" IS NOT NULL
                    AND "ProcessedAt" < CURRENT_TIMESTAMP - (@RetentionSeconds || ' seconds')::interval
                RETURNING 1
            )
            SELECT COUNT(*) FROM deleted;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            sql,
            new { RetentionSeconds = (int)retentionPeriod.TotalSeconds });
    }
}

