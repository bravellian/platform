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

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxCleanupTests : SqlServerTestBase
{
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "dbo", TableName = "Outbox" };

    public OutboxCleanupTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
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

        var oldMessageId = Guid.NewGuid();
        var recentMessageId = Guid.NewGuid();
        var unprocessedMessageId = Guid.NewGuid();

        // Insert old processed message (10 days ago)
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, ProcessedAt, CreatedAt)
            VALUES (@Id, @Topic, @Payload, 1, @ProcessedAt, @CreatedAt)",
            new
            {
                Id = oldMessageId,
                Topic = "Test.Topic",
                Payload = "old message",
                ProcessedAt = DateTimeOffset.UtcNow.AddDays(-10),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-11),
            });

        // Insert recent processed message (1 day ago)
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, ProcessedAt, CreatedAt)
            VALUES (@Id, @Topic, @Payload, 1, @ProcessedAt, @CreatedAt)",
            new
            {
                Id = recentMessageId,
                Topic = "Test.Topic",
                Payload = "recent message",
                ProcessedAt = DateTimeOffset.UtcNow.AddDays(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            });

        // Insert unprocessed message
        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, CreatedAt)
            VALUES (@Id, @Topic, @Payload, 0, @CreatedAt)",
            new
            {
                Id = unprocessedMessageId,
                Topic = "Test.Topic",
                Payload = "unprocessed message",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
            });

        // Act - Run cleanup with 7 day retention
        var retentionSeconds = (int)TimeSpan.FromDays(7).TotalSeconds;
        var deletedCount = await connection.ExecuteScalarAsync<int>(
            $"EXEC [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup] @RetentionSeconds",
            new { RetentionSeconds = retentionSeconds });

        // Assert - Only old processed message should be deleted
        deletedCount.ShouldBe(1);

        var remainingMessages = await connection.QueryAsync<dynamic>(
            $"SELECT Id FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
        var remainingIds = remainingMessages.Select(m => (Guid)m.Id).ToList();

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

        var recentMessageId = Guid.NewGuid();

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, ProcessedAt, CreatedAt)
            VALUES (@Id, @Topic, @Payload, 1, @ProcessedAt, @CreatedAt)",
            new
            {
                Id = recentMessageId,
                Topic = "Test.Topic",
                Payload = "recent message",
                ProcessedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });

        // Act - Run cleanup with 7 day retention
        var retentionSeconds = (int)TimeSpan.FromDays(7).TotalSeconds;
        var deletedCount = await connection.ExecuteScalarAsync<int>(
            $"EXEC [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup] @RetentionSeconds",
            new { RetentionSeconds = retentionSeconds });

        // Assert
        deletedCount.ShouldBe(0);

        var remainingMessages = await connection.QueryAsync<dynamic>(
            $"SELECT Id FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
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
            (Id: Guid.NewGuid(), DaysAgo: 30),
            (Id: Guid.NewGuid(), DaysAgo: 15),
            (Id: Guid.NewGuid(), DaysAgo: 7),
            (Id: Guid.NewGuid(), DaysAgo: 3),
            (Id: Guid.NewGuid(), DaysAgo: 1),
        };

        foreach (var (id, daysAgo) in messages)
        {
            await connection.ExecuteAsync(
                $@"
                INSERT INTO [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}] 
                (Id, Topic, Payload, IsProcessed, ProcessedAt, CreatedAt)
                VALUES (@Id, @Topic, @Payload, 1, @ProcessedAt, @CreatedAt)",
                new
                {
                    Id = id,
                    Topic = "Test.Topic",
                    Payload = $"message {daysAgo} days old",
                    ProcessedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo - 1),
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
            $"SELECT Id FROM [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}]");
        var remainingIds = remainingMessages.Select(m => (Guid)m.Id).ToList();

        remainingIds.Count.ShouldBe(3);
        remainingIds.ShouldNotContain(messages[0].Id); // 30 days
        remainingIds.ShouldNotContain(messages[1].Id); // 15 days
        remainingIds.ShouldContain(messages[2].Id);    // 7 days
        remainingIds.ShouldContain(messages[3].Id);    // 3 days
        remainingIds.ShouldContain(messages[4].Id);    // 1 day
    }

    [Fact]
    public async Task CleanupService_GracefullyHandles_MissingStoredProcedure()
    {
        // Arrange - Create a database without the stored procedure
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Drop the stored procedure if it exists to simulate a database without schema deployment
        await connection.ExecuteAsync($"DROP PROCEDURE IF EXISTS [{this.defaultOptions.SchemaName}].[{this.defaultOptions.TableName}_Cleanup]");

        var mono = new MonotonicClock();
        var logger = new TestLogger<OutboxCleanupService>(this.TestOutputHelper);
        
        // Use very short intervals for testing
        var options = new SqlOutboxOptions
        {
            ConnectionString = this.defaultOptions.ConnectionString,
            SchemaName = this.defaultOptions.SchemaName,
            TableName = this.defaultOptions.TableName,
            RetentionPeriod = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromMilliseconds(100) // Very short interval for testing
        };
        
        var service = new OutboxCleanupService(
            Microsoft.Extensions.Options.Options.Create(options),
            mono,
            logger);

        // Act - Start the service and let it run a few cleanup cycles
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var startTask = service.StartAsync(cts.Token);
        
        // Wait for at least one cleanup attempt
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        
        // Stop the service
        await service.StopAsync(CancellationToken.None);
        
        // Assert - Service should have completed without throwing
        // The ExecuteAsync task should complete successfully (not throw)
        await startTask;
        
        // Verify the stored procedure is still missing (we didn't recreate it)
        var procExists = await connection.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(*) FROM sys.procedures 
               WHERE schema_id = SCHEMA_ID(@SchemaName) 
               AND name = @ProcName",
            new { SchemaName = this.defaultOptions.SchemaName, ProcName = $"{this.defaultOptions.TableName}_Cleanup" });
        procExists.ShouldBe(0);
    }
}
