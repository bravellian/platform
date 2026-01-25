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
    private readonly SqlOutboxOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", TableName = "Outbox" };
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

    /// <summary>When no due messages exist, then ClaimDueAsync returns an empty list.</summary>
    /// <intent>Verify the outbox store does not return items when the queue is empty.</intent>
    /// <scenario>Given an empty outbox table and a SqlOutboxStore instance.</scenario>
    /// <behavior>Then ClaimDueAsync returns zero messages.</behavior>
    [Fact]
    public async Task ClaimDueAsync_WithNoMessages_ReturnsEmptyList()
    {
        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    /// <summary>When a due message exists, then ClaimDueAsync returns it with expected fields.</summary>
    /// <intent>Ensure due outbox rows are claimed and materialized correctly.</intent>
    /// <scenario>Given a ready outbox row created five minutes ago.</scenario>
    /// <behavior>Then ClaimDueAsync returns one message with matching id, topic, and payload.</behavior>
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
            (Id, Topic, Payload, Status, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            });

        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(1);
        messages.First().Id.ShouldBe(OutboxWorkItemIdentifier.From(messageId));
        messages.First().Topic.ShouldBe("Test.Topic");
        messages.First().Payload.ShouldBe("test payload");
    }

    /// <summary>When a message is scheduled for the future, then ClaimDueAsync does not return it.</summary>
    /// <intent>Verify scheduled messages are not claimed before their due time.</intent>
    /// <scenario>Given a ready outbox row with DueTimeUtc set in the future.</scenario>
    /// <behavior>Then ClaimDueAsync returns an empty list.</behavior>
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
            (Id, Topic, Payload, Status, CreatedAt, RetryCount, DueTimeUtc)
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0, @DueTimeUtc)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                DueTimeUtc = DateTime.UtcNow.AddMinutes(10), // Due in the future
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Act
        var messages = await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Assert
        messages.Count.ShouldBe(0);
    }

    /// <summary>When MarkDispatchedAsync is called, then the message is marked Done and processed.</summary>
    /// <intent>Confirm dispatch updates status and IsProcessed.</intent>
    /// <scenario>Given a claimed outbox message id.</scenario>
    /// <behavior>Then the row status is Done and IsProcessed is true.</behavior>
    [Fact]
    public async Task MarkDispatchedAsync_UpdatesMessage()
    {
        // Arrange - Add a message to the outbox and claim it first
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, Status, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Claim the message first
        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        // Act
        await outboxStore!.MarkDispatchedAsync(OutboxWorkItemIdentifier.From(messageId), CancellationToken.None);

        // Assert - Check the message is marked as processed (Status = Done, IsProcessed = 1)
        var result = await connection.QueryFirstAsync(
            $@"
            SELECT Status, IsProcessed FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((byte)result.Status).ShouldBe(OutboxStatus.Done);
        ((bool)result.IsProcessed).ShouldBeTrue();
    }

    /// <summary>When RescheduleAsync is called, then retry count increments and last error is recorded.</summary>
    /// <intent>Verify rescheduling updates retry metadata and leaves the item ready.</intent>
    /// <scenario>Given a claimed outbox message with RetryCount = 2 and a backoff delay.</scenario>
    /// <behavior>Then RetryCount becomes 3, LastError is set, and Status is Ready.</behavior>
    [Fact]
    public async Task RescheduleAsync_UpdatesRetryCountAndNextAttempt()
    {
        // Arrange - Add a message to the outbox and claim it
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, Status, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 2)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Claim the message first
        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        var delay = TimeSpan.FromMinutes(5);
        var errorMessage = "Test error";

        // Act
        await outboxStore!.RescheduleAsync(OutboxWorkItemIdentifier.From(messageId), delay, errorMessage, CancellationToken.None);

        // Assert - Check the message is updated
        var result = await connection.QueryFirstAsync(
            $@"
            SELECT RetryCount, LastError, Status FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((int)result.RetryCount).ShouldBe(3); // Should be incremented from 2 to 3
        ((string)result.LastError).ShouldBe(errorMessage);
        ((byte)result.Status).ShouldBe(OutboxStatus.Ready); // Should be abandoned (Status Ready)
    }

    /// <summary>When FailAsync is called, then the message is marked Failed with error details.</summary>
    /// <intent>Ensure permanent failures set status and error metadata.</intent>
    /// <scenario>Given a claimed outbox message and an error message.</scenario>
    /// <behavior>Then the row status is Failed and LastError/ProcessedBy are populated.</behavior>
    [Fact]
    public async Task FailAsync_MarksMessageAsFailed()
    {
        // Arrange - Add a message to the outbox and claim it
        var messageId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $@"
            INSERT INTO [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            (Id, Topic, Payload, Status, CreatedAt, RetryCount)
            VALUES (@Id, @Topic, @Payload, @Status, @CreatedAt, 0)",
            new
            {
                Id = messageId,
                Topic = "Test.Topic",
                Payload = "test payload",
                Status = OutboxStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        // Claim the message first
        await outboxStore!.ClaimDueAsync(10, CancellationToken.None);

        var errorMessage = "Permanent failure";

        // Act
        await outboxStore!.FailAsync(OutboxWorkItemIdentifier.From(messageId), errorMessage, CancellationToken.None);

        // Assert - Check the message is marked as failed
        var result = await connection.QueryFirstAsync(
            $@"
            SELECT Status, LastError, ProcessedBy FROM [{defaultOptions.SchemaName}].[{defaultOptions.TableName}] 
            WHERE Id = @Id", new { Id = messageId });

        ((byte)result.Status).ShouldBe(OutboxStatus.Failed);
        ((string)result.LastError).ShouldBe(errorMessage);
        ((string)result.ProcessedBy).ShouldContain("FAILED");
    }
}
