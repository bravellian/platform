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
using Dapper;
using Microsoft.Data.SqlClient;

public class InboxCleanupTests : SqlServerTestBase
{
    private readonly SqlInboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "dbo", TableName = "Inbox" };

    public InboxCleanupTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        this.defaultOptions.ConnectionString = this.ConnectionString;
    }

    [Fact]
    public async Task Cleanup_DeletesOldProcessedMessages()
    {
        // Arrange - Add old processed messages and recent processed messages
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var oldMessageId = "old-message-123";
        var recentMessageId = "recent-message-456";
        var unprocessedMessageId = "unprocessed-message-789";

        // Insert old processed message (10 days ago)
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (MessageId, Source, Status, ProcessedUtc, FirstSeenUtc, LastSeenUtc, Attempts)
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)",
            new
            {
                MessageId = oldMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddDays(-10),
                FirstSeenUtc = DateTime.UtcNow.AddDays(-11),
                LastSeenUtc = DateTime.UtcNow.AddDays(-10),
            });

        // Insert recent processed message (1 day ago)
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (MessageId, Source, Status, ProcessedUtc, FirstSeenUtc, LastSeenUtc, Attempts)
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)",
            new
            {
                MessageId = recentMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddDays(-1),
                FirstSeenUtc = DateTime.UtcNow.AddDays(-2),
                LastSeenUtc = DateTime.UtcNow.AddDays(-1),
            });

        // Insert unprocessed message
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (MessageId, Source, Status, FirstSeenUtc, LastSeenUtc, Attempts)
            VALUES (@MessageId, @Source, 'Seen', @FirstSeenUtc, @LastSeenUtc, 0)",
            new
            {
                MessageId = unprocessedMessageId,
                Source = "Test.Source",
                FirstSeenUtc = DateTime.UtcNow.AddDays(-15),
                LastSeenUtc = DateTime.UtcNow.AddDays(-15),
            });

        // Act - Run cleanup with 7 day retention
        var retentionSeconds = (int)TimeSpan.FromDays(7).TotalSeconds;
        var deletedCount = await connection.ExecuteScalarAsync<int>(
            $"EXEC [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup] @RetentionSeconds",
            new { RetentionSeconds = retentionSeconds });

        // Assert - Only old processed message should be deleted
        deletedCount.ShouldBe(1);

        var remainingMessages = await connection.QueryAsync<dynamic>(
            $"SELECT MessageId FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
        var remainingIds = remainingMessages.Select(m => (string)m.MessageId).ToList();

        remainingIds.ShouldNotContain(oldMessageId);
        remainingIds.ShouldContain(recentMessageId);
        remainingIds.ShouldContain(unprocessedMessageId);
    }

    [Fact]
    public async Task Cleanup_WithNoOldMessages_DeletesNothing()
    {
        // Arrange - Add only recent processed messages
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var recentMessageId = "recent-message-123";

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (MessageId, Source, Status, ProcessedUtc, FirstSeenUtc, LastSeenUtc, Attempts)
            VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)",
            new
            {
                MessageId = recentMessageId,
                Source = "Test.Source",
                ProcessedUtc = DateTime.UtcNow.AddHours(-1),
                FirstSeenUtc = DateTime.UtcNow.AddHours(-2),
                LastSeenUtc = DateTime.UtcNow.AddHours(-1),
            });

        // Act - Run cleanup with 7 day retention
        var retentionSeconds = (int)TimeSpan.FromDays(7).TotalSeconds;
        var deletedCount = await connection.ExecuteScalarAsync<int>(
            $"EXEC [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup] @RetentionSeconds",
            new { RetentionSeconds = retentionSeconds });

        // Assert
        deletedCount.ShouldBe(0);

        var remainingMessages = await connection.QueryAsync<dynamic>(
            $"SELECT MessageId FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
        remainingMessages.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Cleanup_RespectsRetentionPeriod()
    {
        // Arrange - Add messages at various ages
        await using var connection = new SqlConnection(this.ConnectionString);
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
                $@"
                INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
                (MessageId, Source, Status, ProcessedUtc, FirstSeenUtc, LastSeenUtc, Attempts)
                VALUES (@MessageId, @Source, 'Done', @ProcessedUtc, @FirstSeenUtc, @LastSeenUtc, 1)",
                new
                {
                    MessageId = messageId,
                    Source = "Test.Source",
                    ProcessedUtc = DateTime.UtcNow.AddDays(-daysAgo),
                    FirstSeenUtc = DateTime.UtcNow.AddDays(-daysAgo - 1),
                    LastSeenUtc = DateTime.UtcNow.AddDays(-daysAgo),
                });
        }

        // Act - Run cleanup with 10 day retention
        var retentionSeconds = (int)TimeSpan.FromDays(10).TotalSeconds;
        var deletedCount = await connection.ExecuteScalarAsync<int>(
            $"EXEC [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup] @RetentionSeconds",
            new { RetentionSeconds = retentionSeconds });

        // Assert - Should delete 30 and 15 day old messages
        deletedCount.ShouldBe(2);

        var remainingMessages = await connection.QueryAsync<dynamic>(
            $"SELECT MessageId FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
        var remainingIds = remainingMessages.Select(m => (string)m.MessageId).ToList();

        remainingIds.Count.ShouldBe(3);
        remainingIds.ShouldNotContain(messages[0].MessageId); // 30 days
        remainingIds.ShouldNotContain(messages[1].MessageId); // 15 days
        remainingIds.ShouldContain(messages[2].MessageId);    // 7 days
        remainingIds.ShouldContain(messages[3].MessageId);    // 3 days
        remainingIds.ShouldContain(messages[4].MessageId);    // 1 day
    }
}
