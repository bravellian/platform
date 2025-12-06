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
        SchemaName = "dbo",
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
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "dbo", "Outbox");
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "dbo");

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
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var messageId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.Outbox (Id, Topic, Payload, MessageId) VALUES (@Id, @Topic, @Payload, @MessageId)",
            new { Id = Guid.NewGuid(), Topic = "test.topic", Payload = "{}", MessageId = messageId });

        return OutboxMessageIdentifier.From(messageId);
    }

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
            "SELECT COUNT(*) FROM dbo.OutboxJoinMember WHERE JoinId = @JoinId AND OutboxMessageId = @MessageId",
            new { JoinId = join.JoinId, MessageId = messageId });

        count.ShouldBe(1);
    }

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
            "SELECT COUNT(*) FROM dbo.OutboxJoinMember WHERE JoinId = @JoinId AND OutboxMessageId = @MessageId",
            new { JoinId = join.JoinId, MessageId = messageId });

        count.ShouldBe(1);
    }

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
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        await connection.ExecuteAsync(
            "[dbo].[Outbox_Claim]",
            new { OwnerToken = ownerToken, LeaseSeconds = 30, BatchSize = 10 },
            commandType: System.Data.CommandType.StoredProcedure);
    }

    private async Task AckMessageAsync(Bravellian.Platform.OwnerToken ownerToken, OutboxMessageIdentifier messageId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var idsTable = CreateGuidIdTable(new[] { messageId.Value });
        using var command = new SqlCommand("[dbo].[Outbox_Ack]", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
        };
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
        parameter.SqlDbType = System.Data.SqlDbType.Structured;
        parameter.TypeName = "[dbo].[GuidIdList]";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private async Task FailMessageAsync(Bravellian.Platform.OwnerToken ownerToken, OutboxMessageIdentifier messageId, string error)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var idsTable = CreateGuidIdTable(new[] { messageId.Value });
        using var command = new SqlCommand("[dbo].[Outbox_Fail]", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
        };
        command.Parameters.AddWithValue("@OwnerToken", ownerToken);
        command.Parameters.AddWithValue("@LastError", error);
        command.Parameters.AddWithValue("@ProcessedBy", "TestMachine");
        var parameter = command.Parameters.AddWithValue("@Ids", idsTable);
        parameter.SqlDbType = System.Data.SqlDbType.Structured;
        parameter.TypeName = "[dbo].[GuidIdList]";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
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
