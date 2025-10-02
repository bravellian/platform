namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Integration test to demonstrate the complete Inbox functionality working end-to-end.
/// </summary>
public class InboxIntegrationTests : SqlServerTestBase
{
    public InboxIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task CompleteInboxWorkflow_DirectServiceUsage_WorksEndToEnd()
    {
        // Arrange - Create service directly with options
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxService>(this.TestOutputHelper);
        var inbox = new SqlInboxService(options, logger);

        var messageId = "integration-test-message";
        var source = "IntegrationTestSource";
        var hash = System.Text.Encoding.UTF8.GetBytes("test-content-hash");

        // Act & Assert - First processing attempt
        var alreadyProcessed1 = await inbox.AlreadyProcessedAsync(messageId, source, hash);
        Assert.False(alreadyProcessed1, "First check should return false");

        // Simulate processing workflow
        await inbox.MarkProcessingAsync(messageId);

        // Complete processing
        await inbox.MarkProcessedAsync(messageId);

        // Subsequent attempts should return true
        var alreadyProcessed2 = await inbox.AlreadyProcessedAsync(messageId, source, hash);
        Assert.True(alreadyProcessed2, "Subsequent check should return true");

        // Verify the message state in database
        await VerifyMessageState(messageId, "Done", processedUtc: true);
    }

    [Fact]
    public async Task PoisonMessageWorkflow_MarkingAsDead_WorksCorrectly()
    {
        // Arrange
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxService>(this.TestOutputHelper);
        var inbox = new SqlInboxService(options, logger);

        var messageId = "poison-test-message";
        var source = "PoisonTestSource";

        // Act - Simulate failed processing workflow
        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source);
        Assert.False(alreadyProcessed);

        await inbox.MarkProcessingAsync(messageId);

        // Mark as dead (poison message)
        await inbox.MarkDeadAsync(messageId);

        // Assert - Verify state
        await VerifyMessageState(messageId, "Dead", processedUtc: false);
    }

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
                var options = Options.Create(new SqlInboxOptions
                {
                    ConnectionString = this.ConnectionString,
                    SchemaName = "dbo",
                    TableName = "Inbox",
                });

                var logger = new TestLogger<SqlInboxService>(this.TestOutputHelper);
                var inboxInstance = new SqlInboxService(options, logger);
                return await inboxInstance.AlreadyProcessedAsync(messageId, source);
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return false (not processed) but only one record should exist
        Assert.All(results, result => Assert.False(result));

        // Verify only one record exists and attempts were tracked
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        var (count, attempts) = await connection.QuerySingleAsync<(int Count, int Attempts)>(
            "SELECT COUNT(*) as Count, MAX(Attempts) as Attempts FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);
        Assert.Equal(concurrentTasks, attempts);
    }

    private async Task VerifyMessageState(string messageId, string expectedStatus, bool processedUtc)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        var result = await connection.QuerySingleAsync<(string Status, DateTime? ProcessedUtc)>(
            "SELECT Status, ProcessedUtc FROM dbo.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

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