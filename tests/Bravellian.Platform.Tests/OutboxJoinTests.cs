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
public class OutboxJoinTests : SqlServerTestBase
{
    private SqlOutboxJoinStore? joinStore;
    private SqlOutboxService? outboxService;
    private readonly SqlOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox"
    };

    public OutboxJoinTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;

        // Ensure schemas exist
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        joinStore = new SqlOutboxJoinStore(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxJoinStore>.Instance);

        outboxService = new SqlOutboxService(
            Options.Create(defaultOptions),
            NullLogger<SqlOutboxService>.Instance,
            joinStore);
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
                "INSERT INTO infra.Outbox (Id, Topic, Payload, MessageId) VALUES (@Id, @Topic, @Payload, @MessageId)",
                new { Id = id, Topic = "test.topic", Payload = "{}", MessageId = Guid.NewGuid() }).ConfigureAwait(false);

            return OutboxMessageIdentifier.From(id);
        }
    }

    /// <summary>When creating a join with tenant, expected steps, and metadata, then the join is pending with zero counts.</summary>
    /// <intent>Verify CreateJoinAsync persists default join state.</intent>
    /// <scenario>Create a join with tenantId 12345, expectedSteps 5, and metadata.</scenario>
    /// <behavior>The join has a non-empty id, pending status, zero completed/failed, metadata, and a recent CreatedUtc.</behavior>
    [Fact]
    public async Task CreateJoinAsync_WithValidParameters_CreatesJoin()
    {
        // Arrange
        long tenantId = 12345;
        int expectedSteps = 5;
        string metadata = """{"type": "etl-workflow", "name": "customer-data-import"}""";

        // Act
        var join = await joinStore!.CreateJoinAsync(
            tenantId,
            expectedSteps,
            metadata,
            CancellationToken.None);

        // Assert
        join.ShouldNotBeNull();
        join.JoinId.ShouldNotBe(JoinIdentifier.Empty);
        join.TenantId.ShouldBe(tenantId);
        join.ExpectedSteps.ShouldBe(expectedSteps);
        join.CompletedSteps.ShouldBe(0);
        join.FailedSteps.ShouldBe(0);
        join.Status.ShouldBe(JoinStatus.Pending);
        join.Metadata.ShouldBe(metadata);
        join.CreatedUtc.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    /// <summary>When a join exists, then GetJoinAsync returns it with the expected steps.</summary>
    /// <intent>Ensure joins can be retrieved by id.</intent>
    /// <scenario>Create a join with expectedSteps 3 and fetch by JoinId.</scenario>
    /// <behavior>The returned join is not null and matches JoinId and ExpectedSteps.</behavior>
    [Fact]
    public async Task GetJoinAsync_WithExistingJoin_ReturnsJoin()
    {
        // Arrange
        var createdJoin = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        // Act
        var retrievedJoin = await joinStore.GetJoinAsync(
            createdJoin.JoinId,
            CancellationToken.None);

        // Assert
        retrievedJoin.ShouldNotBeNull();
        retrievedJoin.JoinId.ShouldBe(createdJoin.JoinId);
        retrievedJoin.ExpectedSteps.ShouldBe(3);
    }

    /// <summary>When a join id is unknown, then GetJoinAsync returns null.</summary>
    /// <intent>Confirm missing joins return no result.</intent>
    /// <scenario>Generate a new JoinIdentifier without creating a join.</scenario>
    /// <behavior>The retrieved join is null.</behavior>
    [Fact]
    public async Task GetJoinAsync_WithNonExistentJoin_ReturnsNull()
    {
        // Arrange
        var nonExistentJoinId = JoinIdentifier.GenerateNew();

        // Act
        var join = await joinStore!.GetJoinAsync(
            nonExistentJoinId,
            CancellationToken.None);

        // Assert
        join.ShouldBeNull();
    }

    /// <summary>When attaching an outbox message to a join, then a join-member row is created.</summary>
    /// <intent>Verify join-member mappings are persisted.</intent>
    /// <scenario>Create a join and an outbox message, then attach the message to the join.</scenario>
    /// <behavior>The OutboxJoinMember table contains one row for the join/message pair.</behavior>
    [Fact]
    public async Task AttachMessageToJoinAsync_WithValidIds_CreatesAssociation()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        // Act
        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Assert - verify association exists in database
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM infra.OutboxJoinMember WHERE JoinId = @JoinId AND OutboxMessageId = @MessageId",
            new { JoinId = join.JoinId, MessageId = messageId });

        count.ShouldBe(1);
    }

    /// <summary>When attaching the same message twice, then only one join-member row exists.</summary>
    /// <intent>Ensure attaching a message is idempotent.</intent>
    /// <scenario>Create a join and message, then call AttachMessageToJoinAsync twice.</scenario>
    /// <behavior>The OutboxJoinMember table still contains a single row for the pair.</behavior>
    [Fact]
    public async Task AttachMessageToJoinAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        // Act - attach the same message twice
        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Assert - verify only one association exists
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM infra.OutboxJoinMember WHERE JoinId = @JoinId AND OutboxMessageId = @MessageId",
            new { JoinId = join.JoinId, MessageId = messageId });

        count.ShouldBe(1);
    }

    /// <summary>When a joined message is marked completed, then CompletedSteps increments and FailedSteps stays zero.</summary>
    /// <intent>Verify completed step increments update the join counters.</intent>
    /// <scenario>Create a join with expected steps, attach a message, then increment completed.</scenario>
    /// <behavior>The join reports CompletedSteps 1, FailedSteps 0, and a newer LastUpdatedUtc.</behavior>
    [Fact]
    public async Task IncrementCompletedAsync_WithValidMessage_IncrementsCount()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Act
        var updatedJoin = await joinStore.IncrementCompletedAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Assert
        updatedJoin.CompletedSteps.ShouldBe(1);
        updatedJoin.FailedSteps.ShouldBe(0);
        updatedJoin.LastUpdatedUtc.ShouldBeGreaterThan(join.LastUpdatedUtc);
    }

    /// <summary>When a joined message is marked failed, then FailedSteps increments and CompletedSteps stays zero.</summary>
    /// <intent>Verify failed step increments update the join counters.</intent>
    /// <scenario>Create a join with expected steps, attach a message, then increment failed.</scenario>
    /// <behavior>The join reports FailedSteps 1, CompletedSteps 0, and a newer LastUpdatedUtc.</behavior>
    [Fact]
    public async Task IncrementFailedAsync_WithValidMessage_IncrementsCount()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Act
        var updatedJoin = await joinStore.IncrementFailedAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        // Assert
        updatedJoin.CompletedSteps.ShouldBe(0);
        updatedJoin.FailedSteps.ShouldBe(1);
        updatedJoin.LastUpdatedUtc.ShouldBeGreaterThan(join.LastUpdatedUtc);
    }

    /// <summary>When updating join status, then the stored join status changes to the new value.</summary>
    /// <intent>Verify UpdateStatusAsync persists status changes.</intent>
    /// <scenario>Create a join, update status to Completed, then fetch the join.</scenario>
    /// <behavior>The retrieved join has Status set to Completed.</behavior>
    [Fact]
    public async Task UpdateStatusAsync_WithValidStatus_UpdatesJoinStatus()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);

        // Act
        await joinStore.UpdateStatusAsync(
            join.JoinId,
            JoinStatus.Completed,
            CancellationToken.None);

        // Assert
        var updatedJoin = await joinStore.GetJoinAsync(
            join.JoinId,
            CancellationToken.None);

        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Completed);
    }

    /// <summary>When multiple messages are attached to a join, then GetJoinMessagesAsync returns all message ids.</summary>
    /// <intent>Ensure join-member retrieval returns every attached message.</intent>
    /// <scenario>Create a join, attach three messages, then query join messages.</scenario>
    /// <behavior>The result contains three ids including the attached message ids.</behavior>
    [Fact]
    public async Task GetJoinMessagesAsync_WithMultipleMessages_ReturnsAllMessageIds()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();
        var messageId3 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId3, CancellationToken.None);

        // Act
        var messageIds = await joinStore.GetJoinMessagesAsync(
            join.JoinId,
            CancellationToken.None);

        // Assert
        messageIds.Count.ShouldBe(3);
        messageIds.ShouldContain(messageId1);
        messageIds.ShouldContain(messageId2);
        messageIds.ShouldContain(messageId3);
    }

    /// <summary>When all steps complete and status is set to Completed, then the join shows all completed and no failures.</summary>
    /// <intent>Verify end-to-end completion updates counts and status.</intent>
    /// <scenario>Create a join with three steps, attach three messages, increment completed for each, then set status to Completed.</scenario>
    /// <behavior>The final join has Status Completed, CompletedSteps 3, and FailedSteps 0.</behavior>
    [Fact]
    public async Task CompleteJoinWorkflow_WithAllStepsCompleted_WorksCorrectly()
    {
        // Arrange - Create a join with 3 expected steps
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            """{"workflow": "test"}""",
            CancellationToken.None);

        var messageIds = new[] {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        // Act - Complete all steps
        foreach (var messageId in messageIds)
        {
            await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);
        }

        // Verify all steps completed
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(3);
        updatedJoin.FailedSteps.ShouldBe(0);

        // Mark as completed
        await joinStore.UpdateStatusAsync(join.JoinId, JoinStatus.Completed, CancellationToken.None);

        // Assert
        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.Status.ShouldBe(JoinStatus.Completed);
        finalJoin.CompletedSteps.ShouldBe(3);
        finalJoin.FailedSteps.ShouldBe(0);
    }

    /// <summary>When some steps fail and status is set to Failed, then the join records completed and failed counts.</summary>
    /// <intent>Verify mixed completion and failure tracking.</intent>
    /// <scenario>Create a join with three steps, complete two, fail one, then set status to Failed.</scenario>
    /// <behavior>The final join has Status Failed, CompletedSteps 2, and FailedSteps 1.</behavior>
    [Fact]
    public async Task CompleteJoinWorkflow_WithSomeStepsFailed_WorksCorrectly()
    {
        // Arrange - Create a join with 3 expected steps
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        var messageIds = new[] {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        // Act - Complete 2 steps, fail 1 step
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[0], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[1], CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageIds[2], CancellationToken.None);

        // Mark as failed
        await joinStore.UpdateStatusAsync(join.JoinId, JoinStatus.Failed, CancellationToken.None);

        // Assert
        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.Status.ShouldBe(JoinStatus.Failed);
        finalJoin.CompletedSteps.ShouldBe(2);
        finalJoin.FailedSteps.ShouldBe(1);
    }

    /// <summary>When IncrementCompletedAsync is called twice for the same message, then the completed count increases only once.</summary>
    /// <intent>Ensure completed increments are idempotent per message.</intent>
    /// <scenario>Create a join, attach a message, then call IncrementCompletedAsync twice.</scenario>
    /// <behavior>The join reports CompletedSteps 1 and FailedSteps 0.</behavior>
    [Fact]
    public async Task IncrementCompletedAsync_CalledTwiceForSameMessage_IsIdempotent()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        // Act - Call increment twice for the same message
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);

        // Assert - Should only increment once
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(1);
        updatedJoin.FailedSteps.ShouldBe(0);
    }

    /// <summary>When IncrementFailedAsync is called twice for the same message, then the failed count increases only once.</summary>
    /// <intent>Ensure failed increments are idempotent per message.</intent>
    /// <scenario>Create a join, attach a message, then call IncrementFailedAsync twice.</scenario>
    /// <behavior>The join reports FailedSteps 1 and CompletedSteps 0.</behavior>
    [Fact]
    public async Task IncrementFailedAsync_CalledTwiceForSameMessage_IsIdempotent()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        // Act - Call increment twice for the same message
        await joinStore.IncrementFailedAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageId, CancellationToken.None);

        // Assert - Should only increment once
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(0);
        updatedJoin.FailedSteps.ShouldBe(1);
    }

    /// <summary>When completed increments exceed expected steps, then the completed count stops at the expected total.</summary>
    /// <intent>Ensure completed steps are capped by expected steps.</intent>
    /// <scenario>Create a join expecting two steps, attach three messages, then increment completed for all three.</scenario>
    /// <behavior>The join reports CompletedSteps 2 and FailedSteps 0.</behavior>
    [Fact]
    public async Task IncrementCompletedAsync_WhenTotalWouldExceedExpected_DoesNotOverCount()
    {
        // Arrange
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageIds = new[] {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        // Act - Try to increment 3 times when only 2 expected
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[0], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[1], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[2], CancellationToken.None);

        // Assert - Should stop at expected count
        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(2);
        updatedJoin.FailedSteps.ShouldBe(0);
    }

    /// <summary>When outbox messages are acknowledged, then join completed counts increment per message.</summary>
    /// <intent>Verify outbox ack integration updates join progress.</intent>
    /// <scenario>Create a join with two messages, claim and ack each via stored procedures.</scenario>
    /// <behavior>CompletedSteps increases to 1 after the first ack and to 2 after the second with no failures.</behavior>
    [Fact]
    public async Task OutboxAck_AutomaticallyReportsJoinCompletion()
    {
        // Arrange - Create a join with 2 expected steps
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        // Create two outbox messages and attach them to the join
        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);

        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Claim and acknowledge the first message
        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId1);

        // Assert - Join should have 1 completed step
        var joinAfterFirst = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterFirst.ShouldNotBeNull();
        joinAfterFirst!.CompletedSteps.ShouldBe(1);
        joinAfterFirst.FailedSteps.ShouldBe(0);

        // Claim and acknowledge the second message
        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId2);

        // Assert - Join should have 2 completed steps
        var joinAfterSecond = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterSecond.ShouldNotBeNull();
        joinAfterSecond!.CompletedSteps.ShouldBe(2);
        joinAfterSecond.FailedSteps.ShouldBe(0);
    }

    /// <summary>When an outbox message is failed, then join failure count increments alongside completed work.</summary>
    /// <intent>Verify outbox failure integration updates join failure counts.</intent>
    /// <scenario>Create a join with two messages, ack the first, then fail the second via stored procedure.</scenario>
    /// <behavior>The join reports CompletedSteps 1 and FailedSteps 1.</behavior>
    [Fact]
    public async Task OutboxFail_AutomaticallyReportsJoinFailure()
    {
        // Arrange - Create a join with 2 expected steps
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        // Create two outbox messages and attach them to the join
        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);

        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Claim and acknowledge the first message (success)
        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId1);

        // Claim and fail the second message
        await ClaimMessagesAsync(ownerToken);
        await FailMessageAsync(ownerToken, messageId2, "Test error");

        // Assert - Join should have 1 completed step and 1 failed step
        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.CompletedSteps.ShouldBe(1);
        finalJoin.FailedSteps.ShouldBe(1);
    }

    /// <summary>When the same message is acknowledged twice, then the join completed count does not increment again.</summary>
    /// <intent>Ensure duplicate acks do not double-count join completion.</intent>
    /// <scenario>Create a join with one message, ack it once, then ack it again with the same owner token.</scenario>
    /// <behavior>The join still reports CompletedSteps 1 and FailedSteps 0 after the second ack.</behavior>
    [Fact]
    public async Task OutboxAck_MultipleAcksForSameMessage_IsIdempotent()
    {
        // Arrange - Create a join with 1 expected step
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);

        // Create a message and attach it to the join
        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Claim and acknowledge the message
        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId);

        // Assert - Join should have 1 completed step
        var joinAfterFirst = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterFirst.ShouldNotBeNull();
        joinAfterFirst!.CompletedSteps.ShouldBe(1);
        joinAfterFirst.FailedSteps.ShouldBe(0);

        // Act - Try to acknowledge the same message again (simulating retry or race condition)
        // Note: The Ack procedure requires OwnerToken and Status = 1, so this won't actually
        // update the outbox message (already processed), but we're testing that the join
        // counter doesn't get incremented again
        await AckMessageAsync(ownerToken, messageId);

        // Assert - Join should still have only 1 completed step (idempotent)
        var joinAfterSecond = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterSecond.ShouldNotBeNull();
        joinAfterSecond!.CompletedSteps.ShouldBe(1);
        joinAfterSecond.FailedSteps.ShouldBe(0);
    }

    // Helper methods for test cleanup
    private async Task ClaimMessagesAsync(Bravellian.Platform.OwnerToken ownerToken)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await connection.ExecuteAsync(
                "[infra].[Outbox_Claim]",
                new { OwnerToken = ownerToken.Value, LeaseSeconds = 30, BatchSize = 10 },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);
        }
    }

    private async Task AckMessageAsync(Bravellian.Platform.OwnerToken ownerToken, OutboxMessageIdentifier messageId)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            var idsTable = CreateGuidIdTable(new[] { messageId.Value });
            using var command = new SqlCommand("[infra].[Outbox_Ack]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = "[infra].[GuidIdList]";
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FailMessageAsync(Bravellian.Platform.OwnerToken ownerToken, OutboxMessageIdentifier messageId, string error)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            var idsTable = CreateGuidIdTable(new[] { messageId.Value });
            using var command = new SqlCommand("[infra].[Outbox_Fail]", connection)
            {
                CommandType = System.Data.CommandType.StoredProcedure,
            };
            command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
            command.Parameters.AddWithValue("@LastError", error);
            command.Parameters.AddWithValue("@ProcessedBy", "TestMachine");
            var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
            parameter.SqlDbType = System.Data.SqlDbType.Structured;
            parameter.TypeName = "[infra].[GuidIdList]";
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static System.Data.DataTable CreateGuidIdTable(IEnumerable<Guid> ids)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("Id", typeof(Guid));

        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
    }
}

