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
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class JoinWaitHandlerTests : SqlServerTestBase
{
    private SqlOutboxJoinStore? joinStore;
    private SqlOutboxService? outbox;
    private JoinWaitHandler? handler;
    private readonly SqlOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "dbo",
        TableName = "Outbox"
    };

    public JoinWaitHandlerTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;

        // Ensure schemas exist
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "dbo", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "dbo").ConfigureAwait(false);

        joinStore = new SqlOutboxJoinStore(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxJoinStore>.Instance);

        outbox = new SqlOutboxService(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxService>.Instance,
            joinStore);

        handler = new JoinWaitHandler(
            joinStore,
            NullLogger<JoinWaitHandler>.Instance,
            outbox);
    }

    // Helper method to create an outbox message and return its ID
    private async Task<OutboxMessageIdentifier> CreateOutboxMessageAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            var id = Guid.NewGuid();
            await connection.ExecuteAsync(
                "INSERT INTO dbo.Outbox (Id, Topic, Payload, MessageId) VALUES (@Id, @Topic, @Payload, @MessageId)",
                new { Id = id, Topic = "test.topic", Payload = "{}", MessageId = Guid.NewGuid() }).ConfigureAwait(false);

            return OutboxMessageIdentifier.From(id);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenJoinNotReady_ThrowsJoinNotReadyException()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3, // Expecting 3 steps
            null,
            CancellationToken.None);

        // Complete only 1 step
        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act & Assert
        await Should.ThrowAsync<JoinNotReadyException>(async () =>
            await handler!.HandleAsync(message, CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task HandleAsync_WhenAllStepsCompleted_MarksJoinAsCompleted()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2, // Expecting 2 steps
            null,
            CancellationToken.None);

        // Complete both steps
        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId2, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act
        await handler!.HandleAsync(message, CancellationToken.None);

        // Assert
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Completed);
    }

    [Fact]
    public async Task HandleAsync_WhenSomeStepsFailed_MarksJoinAsFailed()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2, // Expecting 2 steps
            null,
            CancellationToken.None);

        // Complete 1 step, fail 1 step
        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageId2, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act
        await handler!.HandleAsync(message, CancellationToken.None);

        // Assert
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Failed);
    }

    [Fact]
    public async Task HandleAsync_WhenCompletedSuccessfully_EnqueuesFollowUpMessage()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1, // Expecting 1 step
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true,
            OnCompleteTopic = "etl.start-transform",
            OnCompletePayload = """{"transformId": "123"}"""
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act
        await handler!.HandleAsync(message, CancellationToken.None);

        // Assert - verify follow-up message was enqueued
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Outbox WHERE Topic = @Topic",
            new { Topic = "etl.start-transform" });

        count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_WhenJoinAlreadyCompleted_IsIdempotent()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.UpdateStatusAsync(join.JoinId, JoinStatus.Completed, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = true
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act - handle twice
        await handler!.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);

        // Assert - verify join status unchanged
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Completed);
    }

    [Fact]
    public async Task HandleAsync_WhenFailIfAnyStepFailedIsFalse_CompletesEvenWithFailures()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        // Complete 1 step, fail 1 step
        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageId2, CancellationToken.None);

        var payload = new JoinWaitPayload
        {
            JoinId = join.JoinId,
            FailIfAnyStepFailed = false // Ignore failures
        };

        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "join.wait",
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            MessageId = OutboxMessageIdentifier.GenerateNew()
        };

        // Act
        await handler!.HandleAsync(message, CancellationToken.None);

        // Assert - should complete successfully despite failures
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Completed);
    }
}
