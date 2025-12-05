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
        
        // Ensure join schema exists
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "dbo");
        
        joinStore = new SqlOutboxJoinStore(
            Options.Create(defaultOptions), 
            NullLogger<SqlOutboxJoinStore>.Instance);
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
        join.JoinId.ShouldNotBe(Guid.Empty);
        join.PayeWaiveTenantId.ShouldBe(tenantId);
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
        var nonExistentJoinId = Guid.NewGuid();

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
        var messageId = Guid.NewGuid();

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
        var messageId = Guid.NewGuid();

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
        var messageId = Guid.NewGuid();
        
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
        var messageId = Guid.NewGuid();
        
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
            
        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        var messageId3 = Guid.NewGuid();
        
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
            
        var messageIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        
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
            
        var messageIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        
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
}
