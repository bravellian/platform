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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SqlOutboxStoreTests : SqlServerTestBase
{
    private SqlOutboxStore? outboxStore;
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "dbo", TableName = "Outbox" };
    private FakeTimeProvider timeProvider = default!;

    public SqlOutboxStoreTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        timeProvider = new FakeTimeProvider();
        defaultOptions.ConnectionString = ConnectionString;
        var logger = new TestLogger<SqlOutboxStore>(TestOutputHelper);
        outboxStore = new SqlOutboxStore(Options.Create(defaultOptions), timeProvider, logger);
    }

    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimDueAsync_WithDueMessages_ReturnsMessages()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1), // Due in the past
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(1);
        messages.First().Id.ShouldBe(messageId);
        messages.First().Topic.ShouldBe("Test.Topic");
        messages.First().Payload.ShouldBe("test payload");
    }

    [Fact]
    public async Task ClaimDueAsync_WithFutureMessages_ReturnsEmpty()
    {
        // Arrange - Add a message scheduled for the future
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(10), // Due in the future
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    [Fact]
    public async Task MarkDispatchedAsync_UpdatesMessage()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        await outboxStore!.MarkDispatchedAsync(messageId, CancellationToken.None);

        // Assert - Check the message is marked as processed
        var processed = await connection.QueryFirstAsync<bool>(
            $@"
            SELECT IsProcessed FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        processed.ShouldBeTrue();
    }

    [Fact]
    public async Task RescheduleAsync_UpdatesRetryCountAndNextAttempt()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 2)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var delay = TimeSpan.FromMinutes(5);
        var errorMessage = "Test error";

        // Act
        await outboxStore!.RescheduleAsync(messageId, delay, errorMessage, CancellationToken.None);

        // Assert - Check the message is updated
        var result = await connection.QueryFirstAsync(
            $@"
            SELECT RetryCount, LastError, NextAttemptAt FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((int)result.RetryCount).ShouldBe(3); // Should be incremented from 2 to 3
        ((string)result.LastError).ShouldBe(errorMessage);

        var nextAttempt = (DateTimeOffset)result.NextAttemptAt;
        nextAttempt.ShouldBeGreaterThan(timeProvider.GetUtcNow().Add(delay).AddMinutes(-1));
        nextAttempt.ShouldBeLessThan(timeProvider.GetUtcNow().Add(delay).AddMinutes(1));
    }

    [Fact]
    public async Task FailAsync_MarksMessageAsFailed()
    {
        // Arrange - Add a message to the outbox
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, IsProcessed, NextAttemptAt, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, 0, @NextAttemptAt, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var errorMessage = "Permanent failure";

        // Act
        await outboxStore!.FailAsync(messageId, errorMessage, CancellationToken.None);

        // Assert - Check the message is marked as processed with error
        var result = await connection.QueryFirstAsync(
            $@"
            SELECT IsProcessed, LastError, ProcessedBy FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((bool)result.IsProcessed).ShouldBeTrue();
        ((string)result.LastError).ShouldBe(errorMessage);
        ((string)result.ProcessedBy).ShouldContain("FAILED");
    }
}
