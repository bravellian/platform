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

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SqlInboxServiceTests : SqlServerTestBase
{
    public SqlInboxServiceTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    [Fact]
    public async Task AlreadyProcessedAsync_WithNewMessage_ReturnsFalseAndRecordsMessage()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-1";
        var source = "test-source";

        // Act
        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(alreadyProcessed);

        // Verify the message was recorded in the database
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AlreadyProcessedAsync_WithProcessedMessage_ReturnsTrue()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-2";
        var source = "test-source";

        // First, record and process the message
        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);
        await inbox.MarkProcessedAsync(messageId, TestContext.Current.CancellationToken);

        // Act
        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(alreadyProcessed);
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedUtcAndStatus()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-3";
        var source = "test-source";

        // Record the message first
        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await inbox.MarkProcessedAsync(messageId, TestContext.Current.CancellationToken);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync<(DateTime? ProcessedUtc, string Status)>(
            "SELECT ProcessedUtc, Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.NotNull(result.ProcessedUtc);
        Assert.Equal("Done", result.Status);
    }

    [Fact]
    public async Task MarkProcessingAsync_UpdatesStatus()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-4";
        var source = "test-source";

        // Record the message first
        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await inbox.MarkProcessingAsync(messageId, TestContext.Current.CancellationToken);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal("Processing", status);
    }

    [Fact]
    public async Task MarkDeadAsync_UpdatesStatus()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-5";
        var source = "test-source";

        // Record the message first
        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await inbox.MarkDeadAsync(messageId, TestContext.Current.CancellationToken);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal("Dead", status);
    }

    [Fact]
    public async Task ConcurrentAlreadyProcessedAsync_WithSameMessage_HandledCorrectly()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "concurrent-test-message";
        var source = "test-source";

        // Act - Simulate concurrent calls to AlreadyProcessedAsync
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return false since the message wasn't processed yet
        Assert.All(results, result => Assert.False(result));

        // Verify only one record was created in the database
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);

        // Check that attempts were incremented appropriately
        var attempts = await connection.QuerySingleAsync<int>(
            "SELECT Attempts FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task AlreadyProcessedAsync_WithHash_StoresHashCorrectly()
    {
        // Arrange
        var inbox = CreateInboxService();
        var messageId = "test-message-with-hash";
        var source = "test-source";
        var hash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

        // Act
        await inbox.AlreadyProcessedAsync(messageId, source, hash, TestContext.Current.CancellationToken);

        // Assert
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var storedHash = await connection.QuerySingleAsync<byte[]>(
            "SELECT Hash FROM infra.Inbox WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(hash, storedHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AlreadyProcessedAsync_WithInvalidMessageId_ThrowsArgumentException(string? invalidMessageId)
    {
        // Arrange
        var inbox = CreateInboxService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            inbox.AlreadyProcessedAsync(invalidMessageId!, "test-source", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AlreadyProcessedAsync_WithInvalidSource_ThrowsArgumentException(string? invalidSource)
    {
        // Arrange
        var inbox = CreateInboxService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            inbox.AlreadyProcessedAsync("test-message", invalidSource!, cancellationToken: TestContext.Current.CancellationToken));
    }

    private SqlInboxService CreateInboxService()
    {
        var options = Options.Create(new SqlInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<SqlInboxService>(TestOutputHelper);
        return new SqlInboxService(options, logger);
    }
}
