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

namespace Bravellian.Platform.Tests;
/// <summary>
/// Integration test to demonstrate the complete Inbox functionality working end-to-end.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class InboxIntegrationTests : PostgresTestBase
{
    private readonly string qualifiedInboxTableName = PostgresSqlHelper.Qualify("infra", "Inbox");
    public InboxIntegrationTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    /// <summary>
    /// Given a direct inbox workflow, then AlreadyProcessed transitions from false to true and the row is Done.
    /// </summary>
    /// <intent>
    /// Verify end-to-end inbox processing with direct PostgresInboxService usage.
    /// </intent>
    /// <scenario>
    /// Given a PostgresInboxService configured with infra.Inbox and a test message id.
    /// </scenario>
    /// <behavior>
    /// The first AlreadyProcessedAsync is false, after MarkProcessing/MarkProcessed it is true, and ProcessedUtc is set.
    /// </behavior>
    [Fact]
    public async Task CompleteInboxWorkflow_DirectServiceUsage_WorksEndToEnd()
    {
        // Arrange - Create service directly with options
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
        var inbox = new PostgresInboxService(options, logger);

        var messageId = "integration-test-message";
        var source = "IntegrationTestSource";
        var hash = System.Text.Encoding.UTF8.GetBytes("test-content-hash");

        // Act & Assert - First processing attempt
        var alreadyProcessed1 = await inbox.AlreadyProcessedAsync(messageId, source, hash, TestContext.Current.CancellationToken);
        Assert.False(alreadyProcessed1, "First check should return false");

        // Simulate processing workflow
        await inbox.MarkProcessingAsync(messageId, TestContext.Current.CancellationToken);

        // Complete processing
        await inbox.MarkProcessedAsync(messageId, TestContext.Current.CancellationToken);

        // Subsequent attempts should return true
        var alreadyProcessed2 = await inbox.AlreadyProcessedAsync(messageId, source, hash, TestContext.Current.CancellationToken);
        Assert.True(alreadyProcessed2, "Subsequent check should return true");

        // Verify the message state in database
        await VerifyMessageState(messageId, "Done", processedUtc: true);
    }

    /// <summary>
    /// When a message is marked dead after processing starts, then its status is Dead and ProcessedUtc stays null.
    /// </summary>
    /// <intent>
    /// Verify poison-message handling in the inbox workflow.
    /// </intent>
    /// <scenario>
    /// Given a PostgresInboxService and a message marked Processing then Dead.
    /// </scenario>
    /// <behavior>
    /// The database row shows Status Dead and no ProcessedUtc value.
    /// </behavior>
    [Fact]
    public async Task PoisonMessageWorkflow_MarkingAsDead_WorksCorrectly()
    {
        // Arrange
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
        var inbox = new PostgresInboxService(options, logger);

        var messageId = "poison-test-message";
        var source = "PoisonTestSource";

        // Act - Simulate failed processing workflow
        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(alreadyProcessed);

        await inbox.MarkProcessingAsync(messageId, TestContext.Current.CancellationToken);

        // Mark as dead (poison message)
        await inbox.MarkDeadAsync(messageId, TestContext.Current.CancellationToken);

        // Assert - Verify state
        await VerifyMessageState(messageId, "Dead", processedUtc: false);
    }

    /// <summary>
    /// When multiple threads call AlreadyProcessedAsync concurrently, then one row is created with attempts tracked.
    /// </summary>
    /// <intent>
    /// Verify concurrent deduplication records a single row and increments Attempts.
    /// </intent>
    /// <scenario>
    /// Given ten concurrent tasks invoking AlreadyProcessedAsync for the same message id.
    /// </scenario>
    /// <behavior>
    /// All calls return false, only one row exists, and Attempts equals the task count.
    /// </behavior>
    [Fact]
    public async Task ConcurrentAccess_WithMultipleThreads_HandledSafely()
    {
        // Arrange
        var messageId = "concurrent-test-message";
        var source = "ConcurrentTestSource";
        const int concurrentTasks = 10;

        // Act - Simulate concurrent access from multiple threads
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var options = Options.Create(new PostgresInboxOptions
                {
                    ConnectionString = ConnectionString,
                    SchemaName = "infra",
                    TableName = "Inbox",
                });

                var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
                var inboxInstance = new PostgresInboxService(options, logger);
                return await inboxInstance.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return false (not processed) but only one record should exist
        Assert.All(results, result => Assert.False(result));

        // Verify only one record exists and attempts were tracked
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var (count, attempts) = await connection.QuerySingleAsync<(int Count, int Attempts)>(
            $"SELECT COUNT(*) as Count, MAX(\"Attempts\") as Attempts FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);
        Assert.Equal(concurrentTasks, attempts);
    }

    private async Task VerifyMessageState(string messageId, string expectedStatus, bool processedUtc)
    {
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        var result = await connection.QuerySingleAsync<(string Status, DateTime? ProcessedUtc)>(
            $"SELECT \"Status\", \"ProcessedUtc\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId }).ConfigureAwait(false);

        Assert.Equal(expectedStatus, result.Status);

        if (processedUtc)
        {
            Assert.NotNull(result.ProcessedUtc);
        }
        else
        {
            Assert.Null(result.ProcessedUtc);
        }
    }
}


