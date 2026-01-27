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
public class PostgresInboxServiceTests : PostgresTestBase
{
    private readonly string qualifiedInboxTableName = PostgresSqlHelper.Qualify("infra", "Inbox");

    public PostgresInboxServiceTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    /// <summary>When AlreadyProcessedAsync is called for a new message, then it returns false and records the row.</summary>
    /// <intent>Verify new messages are inserted and reported as not processed.</intent>
    /// <scenario>Given a PostgresInboxService and a new message id/source.</scenario>
    /// <behavior>AlreadyProcessedAsync returns false and one inbox row exists for the message id.</behavior>
    [Fact]
    public async Task AlreadyProcessedAsync_WithNewMessage_ReturnsFalseAndRecordsMessage()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-1";
        var source = "test-source";

        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(alreadyProcessed);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var count = await connection.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);
    }

    /// <summary>When a message has been marked processed, then AlreadyProcessedAsync returns true.</summary>
    /// <intent>Verify processed messages are detected as already processed.</intent>
    /// <scenario>Given a message recorded via AlreadyProcessedAsync and then marked processed.</scenario>
    /// <behavior>A subsequent AlreadyProcessedAsync call returns true.</behavior>
    [Fact]
    public async Task AlreadyProcessedAsync_WithProcessedMessage_ReturnsTrue()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-2";
        var source = "test-source";

        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);
        await inbox.MarkProcessedAsync(messageId, TestContext.Current.CancellationToken);

        var alreadyProcessed = await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(alreadyProcessed);
    }

    /// <summary>When MarkProcessedAsync is called, then ProcessedUtc is set and Status is Done.</summary>
    /// <intent>Verify MarkProcessedAsync updates completion fields.</intent>
    /// <scenario>Given a message recorded via AlreadyProcessedAsync.</scenario>
    /// <behavior>ProcessedUtc is non-null and Status is Done in the inbox row.</behavior>
    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedUtcAndStatus()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-3";
        var source = "test-source";

        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        await inbox.MarkProcessedAsync(messageId, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var result = await connection.QuerySingleAsync<(DateTime? ProcessedUtc, string Status)>(
            $"SELECT \"ProcessedUtc\", \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.NotNull(result.ProcessedUtc);
        Assert.Equal("Done", result.Status);
    }

    /// <summary>When MarkProcessingAsync is called, then Status becomes Processing.</summary>
    /// <intent>Verify MarkProcessingAsync moves the message into processing state.</intent>
    /// <scenario>Given a message recorded via AlreadyProcessedAsync.</scenario>
    /// <behavior>The inbox row Status is Processing.</behavior>
    [Fact]
    public async Task MarkProcessingAsync_UpdatesStatus()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-4";
        var source = "test-source";

        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        await inbox.MarkProcessingAsync(messageId, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            $"SELECT \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal("Processing", status);
    }

    /// <summary>When MarkDeadAsync is called, then Status becomes Dead.</summary>
    /// <intent>Verify MarkDeadAsync marks messages as dead.</intent>
    /// <scenario>Given a message recorded via AlreadyProcessedAsync.</scenario>
    /// <behavior>The inbox row Status is Dead.</behavior>
    [Fact]
    public async Task MarkDeadAsync_UpdatesStatus()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-5";
        var source = "test-source";

        await inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken);

        await inbox.MarkDeadAsync(messageId, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var status = await connection.QuerySingleAsync<string>(
            $"SELECT \"Status\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal("Dead", status);
    }

    /// <summary>When multiple AlreadyProcessedAsync calls run concurrently, then one row exists with attempts tracked.</summary>
    /// <intent>Verify concurrent checks create a single inbox row and increment attempts.</intent>
    /// <scenario>Given five concurrent AlreadyProcessedAsync calls for the same message id.</scenario>
    /// <behavior>All calls return false, one row exists, and Attempts equals 5.</behavior>
    [Fact]
    public async Task ConcurrentAlreadyProcessedAsync_WithSameMessage_HandledCorrectly()
    {
        var inbox = CreateInboxService();
        var messageId = "concurrent-test-message";
        var source = "test-source";

        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(inbox.AlreadyProcessedAsync(messageId, source, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.False(result));

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var count = await connection.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(1, count);

        var attempts = await connection.QuerySingleAsync<int>(
            $"SELECT \"Attempts\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(5, attempts);
    }

    /// <summary>When AlreadyProcessedAsync is called with a hash, then the hash is stored.</summary>
    /// <intent>Verify the message hash is persisted with the inbox row.</intent>
    /// <scenario>Given a message id/source and a byte[] hash passed to AlreadyProcessedAsync.</scenario>
    /// <behavior>The stored Hash value matches the provided hash.</behavior>
    [Fact]
    public async Task AlreadyProcessedAsync_WithHash_StoresHashCorrectly()
    {
        var inbox = CreateInboxService();
        var messageId = "test-message-with-hash";
        var source = "test-source";
        var hash = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

        await inbox.AlreadyProcessedAsync(messageId, source, hash, TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var storedHash = await connection.QuerySingleAsync<byte[]>(
            $"SELECT \"Hash\" FROM {qualifiedInboxTableName} WHERE \"MessageId\" = @MessageId",
            new { MessageId = messageId });

        Assert.Equal(hash, storedHash);
    }

    /// <summary>When AlreadyProcessedAsync receives a null or empty message id, then it throws ArgumentException.</summary>
    /// <intent>Verify invalid message ids are rejected.</intent>
    /// <scenario>Given null or empty messageId inputs.</scenario>
    /// <behavior>AlreadyProcessedAsync throws ArgumentException.</behavior>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AlreadyProcessedAsync_WithInvalidMessageId_ThrowsArgumentException(string? invalidMessageId)
    {
        var inbox = CreateInboxService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            inbox.AlreadyProcessedAsync(invalidMessageId!, "test-source", cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>When AlreadyProcessedAsync receives a null or empty source, then it throws ArgumentException.</summary>
    /// <intent>Verify invalid sources are rejected.</intent>
    /// <scenario>Given null or empty source inputs.</scenario>
    /// <behavior>AlreadyProcessedAsync throws ArgumentException.</behavior>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AlreadyProcessedAsync_WithInvalidSource_ThrowsArgumentException(string? invalidSource)
    {
        var inbox = CreateInboxService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            inbox.AlreadyProcessedAsync("test-message", invalidSource!, cancellationToken: TestContext.Current.CancellationToken));
    }

    private PostgresInboxService CreateInboxService()
    {
        var options = Options.Create(new PostgresInboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
        });

        var logger = new TestLogger<PostgresInboxService>(TestOutputHelper);
        return new PostgresInboxService(options, logger);
    }
}

