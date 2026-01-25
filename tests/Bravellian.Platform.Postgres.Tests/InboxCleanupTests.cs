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
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxCleanupTests : PostgresTestBase
{
    private readonly PostgresInboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Inbox" };
    private string qualifiedTableName = string.Empty;

    public InboxCleanupTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        qualifiedTableName = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);
    }

    /// <summary>
    /// When cleanup runs with a seven-day retention period, then only old processed messages are deleted.
    /// </summary>
    /// <intent>
    /// Verify cleanup deletes processed rows older than the retention window.
    /// </intent>
    /// <scenario>
    /// Given one processed message older than 7 days, one recent processed message, and one unprocessed message.
    /// </scenario>
    /// <behavior>
    /// Cleanup deletes only the old processed row and leaves the recent and unprocessed rows intact.
    /// </behavior>
    [Fact]
    public async Task Cleanup_DeletesOldProcessedMessages()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var oldMessageId = "old-message-123";
        var recentMessageId = "recent-message-456";
        var unprocessedMessageId = "unprocessed-message-789";

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("MessageId", "Source", "Status", "ProcessedUtc", "FirstSeenUtc", "LastSeenUtc", "Attempts")
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)
            """,
            new
            {
                MessageId = oldMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddDays(-10),
                FirstSeenUtc = DateTime.UtcNow.AddDays(-11),
                LastSeenUtc = DateTime.UtcNow.AddDays(-10),
            });

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("MessageId", "Source", "Status", "ProcessedUtc", "FirstSeenUtc", "LastSeenUtc", "Attempts")
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)
            """,
            new
            {
                MessageId = recentMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddDays(-1),
                FirstSeenUtc = DateTime.UtcNow.AddDays(-2),
                LastSeenUtc = DateTime.UtcNow.AddDays(-1),
            });

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("MessageId", "Source", "Status", "FirstSeenUtc", "LastSeenUtc", "Attempts")
            VALUES (@MessageId, @Source, 'Seen', @FirstSeenUtc, @LastSeenUtc, 0)
            """,
            new
            {
                MessageId = unprocessedMessageId,
                Source = "Test.Source",
                FirstSeenUtc = DateTime.UtcNow.AddDays(-15),
                LastSeenUtc = DateTime.UtcNow.AddDays(-15),
            });

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);

        deletedCount.ShouldBe(1);

        var remainingMessages = await connection.QueryAsync<string>(
            $"""
            SELECT "MessageId"
            FROM {qualifiedTableName}
            """);

        var remainingIds = remainingMessages.ToList();
        remainingIds.ShouldNotContain(oldMessageId);
        remainingIds.ShouldContain(recentMessageId);
        remainingIds.ShouldContain(unprocessedMessageId);
    }

    /// <summary>
    /// Given only recent processed messages, then cleanup deletes nothing.
    /// </summary>
    /// <intent>
    /// Verify cleanup leaves processed rows inside the retention window.
    /// </intent>
    /// <scenario>
    /// Given a single processed message with ProcessedUtc within the retention window.
    /// </scenario>
    /// <behavior>
    /// Deleted count is 0 and the row remains.
    /// </behavior>
    [Fact]
    public async Task Cleanup_WithNoOldMessages_DeletesNothing()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var recentMessageId = "recent-message-123";

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {qualifiedTableName}
            ("MessageId", "Source", "Status", "ProcessedUtc", "FirstSeenUtc", "LastSeenUtc", "Attempts")
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)
            """,
            new
            {
                MessageId = recentMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddHours(-1),
                FirstSeenUtc = DateTime.UtcNow.AddHours(-2),
                LastSeenUtc = DateTime.UtcNow.AddHours(-1),
            });

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(7)).ConfigureAwait(false);

        deletedCount.ShouldBe(0);

        var remainingMessages = await connection.QueryAsync<string>(
            $"""
            SELECT "MessageId"
            FROM {qualifiedTableName}
            """);

        remainingMessages.Count().ShouldBe(1);
    }

    /// <summary>
    /// When cleanup uses a 10-day retention period, then only messages older than 10 days are deleted.
    /// </summary>
    /// <intent>
    /// Verify cleanup uses the retention cutoff to select deletions.
    /// </intent>
    /// <scenario>
    /// Given processed messages at 30, 15, 7, 3, and 1 days old.
    /// </scenario>
    /// <behavior>
    /// Two oldest rows are deleted and the three newest rows remain.
    /// </behavior>
    [Fact]
    public async Task Cleanup_RespectsRetentionPeriod()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var messages = new[]
        {
            (MessageId: "msg-30-days", DaysAgo: 30),
            (MessageId: "msg-15-days", DaysAgo: 15),
            (MessageId: "msg-7-days", DaysAgo: 7),
            (MessageId: "msg-3-days", DaysAgo: 3),
            (MessageId: "msg-1-day", DaysAgo: 1),
        };

        foreach (var (messageId, daysAgo) in messages)
        {
            await connection.ExecuteAsync(
                $"""
                INSERT INTO {qualifiedTableName}
                ("MessageId", "Source", "Status", "ProcessedUtc", "FirstSeenUtc", "LastSeenUtc", "Attempts")
                VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)
                """,
                new
                {
                    MessageId = messageId,
                    Source = "Test.Source",
                    ProcessedUtc = DateTime.UtcNow.AddDays(-daysAgo),
                    FirstSeenUtc = DateTime.UtcNow.AddDays(-daysAgo - 1),
                    LastSeenUtc = DateTime.UtcNow.AddDays(-daysAgo),
                });
        }

        var deletedCount = await ExecuteCleanupAsync(TimeSpan.FromDays(10)).ConfigureAwait(false);

        deletedCount.ShouldBe(2);

        var remainingMessages = await connection.QueryAsync<string>(
            $"""
            SELECT "MessageId"
            FROM {qualifiedTableName}
            """);

        var remainingIds = remainingMessages.ToList();
        remainingIds.Count.ShouldBe(3);
        remainingIds.ShouldNotContain(messages[0].MessageId);
        remainingIds.ShouldNotContain(messages[1].MessageId);
        remainingIds.ShouldContain(messages[2].MessageId);
        remainingIds.ShouldContain(messages[3].MessageId);
        remainingIds.ShouldContain(messages[4].MessageId);
    }

    /// <summary>
    /// When the inbox table is missing, then the cleanup service runs without recreating it.
    /// </summary>
    /// <intent>
    /// Verify cleanup handles missing tables without side effects.
    /// </intent>
    /// <scenario>
    /// Given the inbox table is dropped and the cleanup service runs with a short interval.
    /// </scenario>
    /// <behavior>
    /// The service completes and the inbox table still does not exist.
    /// </behavior>
    [Fact]
    public async Task CleanupService_GracefullyHandles_MissingTable()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteAsync($"DROP TABLE IF EXISTS {qualifiedTableName}");

        var mono = new FakeMonotonicClock();
        var logger = new TestLogger<InboxCleanupService>(TestOutputHelper);

        var options = new PostgresInboxOptions
        {
            ConnectionString = defaultOptions.ConnectionString,
            SchemaName = defaultOptions.SchemaName,
            TableName = defaultOptions.TableName,
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromMilliseconds(100),
        };

        var service = new InboxCleanupService(
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
                WHERE "Status" = 'Done'
                    AND "ProcessedUtc" IS NOT NULL
                    AND "ProcessedUtc" < CURRENT_TIMESTAMP - (@RetentionSeconds || ' seconds')::interval
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
