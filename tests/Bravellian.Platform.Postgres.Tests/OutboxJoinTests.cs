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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxJoinTests : PostgresTestBase
{
    private PostgresOutboxJoinStore? joinStore;
    private PostgresOutboxService? outboxService;
    private readonly PostgresOutboxOptions defaultOptions = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox"
    };
    private string outboxTable = string.Empty;
    private string joinMemberTable = string.Empty;

    public OutboxJoinTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;

        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        joinStore = new PostgresOutboxJoinStore(
            Options.Create(defaultOptions),
            NullLogger<PostgresOutboxJoinStore>.Instance);

        outboxService = new PostgresOutboxService(
            Options.Create(defaultOptions),
            NullLogger<PostgresOutboxService>.Instance,
            joinStore);

        outboxTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TableName);
        joinMemberTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, "OutboxJoinMember");
    }

    private async Task<OutboxMessageIdentifier> CreateOutboxMessageAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            $"""
            INSERT INTO {outboxTable} ("Id", "Topic", "Payload", "MessageId")
            VALUES (@Id, @Topic, @Payload, @MessageId)
            """,
            new { Id = id, Topic = "test.topic", Payload = "{}", MessageId = Guid.NewGuid() }).ConfigureAwait(false);

        return OutboxMessageIdentifier.From(id);
    }

    [Fact]
    public async Task CreateJoinAsync_WithValidParameters_CreatesJoin()
    {
        long tenantId = 12345;
        int expectedSteps = 5;
        string metadata = """{"type": "etl-workflow", "name": "customer-data-import"}""";

        var join = await joinStore!.CreateJoinAsync(
            tenantId,
            expectedSteps,
            metadata,
            CancellationToken.None);

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
        var createdJoin = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        var retrievedJoin = await joinStore.GetJoinAsync(
            createdJoin.JoinId,
            CancellationToken.None);

        retrievedJoin.ShouldNotBeNull();
        retrievedJoin!.JoinId.ShouldBe(createdJoin.JoinId);
        retrievedJoin.ExpectedSteps.ShouldBe(3);
    }

    [Fact]
    public async Task GetJoinAsync_WithNonExistentJoin_ReturnsNull()
    {
        var nonExistentJoinId = JoinIdentifier.GenerateNew();

        var join = await joinStore!.GetJoinAsync(
            nonExistentJoinId,
            CancellationToken.None);

        join.ShouldBeNull();
    }

    [Fact]
    public async Task AttachMessageToJoinAsync_WithValidIds_CreatesAssociation()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(*)
            FROM {joinMemberTable}
            WHERE "JoinId" = @JoinId AND "OutboxMessageId" = @MessageId
            """,
            new { JoinId = join.JoinId, MessageId = messageId.Value });

        count.ShouldBe(1);
    }

    [Fact]
    public async Task AttachMessageToJoinAsync_CalledTwice_IsIdempotent()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);
        var messageId = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        await joinStore.AttachMessageToJoinAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>(
            $"""
            SELECT COUNT(*)
            FROM {joinMemberTable}
            WHERE "JoinId" = @JoinId AND "OutboxMessageId" = @MessageId
            """,
            new { JoinId = join.JoinId, MessageId = messageId.Value });

        count.ShouldBe(1);
    }

    [Fact]
    public async Task IncrementCompletedAsync_WithValidMessage_IncrementsCount()
    {
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

        var updatedJoin = await joinStore.IncrementCompletedAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        updatedJoin.CompletedSteps.ShouldBe(1);
        updatedJoin.FailedSteps.ShouldBe(0);
        updatedJoin.LastUpdatedUtc.ShouldBeGreaterThan(join.LastUpdatedUtc);
    }

    [Fact]
    public async Task IncrementFailedAsync_WithValidMessage_IncrementsCount()
    {
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

        var updatedJoin = await joinStore.IncrementFailedAsync(
            join.JoinId,
            messageId,
            CancellationToken.None);

        updatedJoin.CompletedSteps.ShouldBe(0);
        updatedJoin.FailedSteps.ShouldBe(1);
        updatedJoin.LastUpdatedUtc.ShouldBeGreaterThan(join.LastUpdatedUtc);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithValidStatus_UpdatesJoinStatus()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);

        await joinStore.UpdateStatusAsync(
            join.JoinId,
            JoinStatus.Completed,
            CancellationToken.None);

        var updatedJoin = await joinStore.GetJoinAsync(
            join.JoinId,
            CancellationToken.None);

        updatedJoin.ShouldNotBeNull();
        updatedJoin!.Status.ShouldBe(JoinStatus.Completed);
    }

    [Fact]
    public async Task GetJoinMessagesAsync_WithMultipleMessages_ReturnsAllMessageIds()
    {
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

        var messageIds = await joinStore.GetJoinMessagesAsync(
            join.JoinId,
            CancellationToken.None);

        messageIds.Count.ShouldBe(3);
        messageIds.ShouldContain(messageId1);
        messageIds.ShouldContain(messageId2);
        messageIds.ShouldContain(messageId3);
    }

    [Fact]
    public async Task CompleteJoinWorkflow_WithAllStepsCompleted_WorksCorrectly()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            """{"workflow": "test"}""",
            CancellationToken.None);

        var messageIds = new[]
        {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        foreach (var messageId in messageIds)
        {
            await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);
        }

        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(3);
        updatedJoin.FailedSteps.ShouldBe(0);

        await joinStore.UpdateStatusAsync(join.JoinId, JoinStatus.Completed, CancellationToken.None);

        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.Status.ShouldBe(JoinStatus.Completed);
        finalJoin.CompletedSteps.ShouldBe(3);
        finalJoin.FailedSteps.ShouldBe(0);
    }

    [Fact]
    public async Task CompleteJoinWorkflow_WithSomeStepsFailed_WorksCorrectly()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            3,
            null,
            CancellationToken.None);

        var messageIds = new[]
        {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[0], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[1], CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageIds[2], CancellationToken.None);

        await joinStore.UpdateStatusAsync(join.JoinId, JoinStatus.Failed, CancellationToken.None);

        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.Status.ShouldBe(JoinStatus.Failed);
        finalJoin.CompletedSteps.ShouldBe(2);
        finalJoin.FailedSteps.ShouldBe(1);
    }

    [Fact]
    public async Task IncrementCompletedAsync_CalledTwiceForSameMessage_IsIdempotent()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageId, CancellationToken.None);

        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(1);
        updatedJoin.FailedSteps.ShouldBe(0);
    }

    [Fact]
    public async Task IncrementFailedAsync_CalledTwiceForSameMessage_IsIdempotent()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        await joinStore.IncrementFailedAsync(join.JoinId, messageId, CancellationToken.None);
        await joinStore.IncrementFailedAsync(join.JoinId, messageId, CancellationToken.None);

        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(0);
        updatedJoin.FailedSteps.ShouldBe(1);
    }

    [Fact]
    public async Task IncrementCompletedAsync_WhenTotalWouldExceedExpected_DoesNotOverCount()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageIds = new[]
        {
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync(),
            await CreateOutboxMessageAsync()
        };

        foreach (var messageId in messageIds)
        {
            await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);
        }

        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[0], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[1], CancellationToken.None);
        await joinStore.IncrementCompletedAsync(join.JoinId, messageIds[2], CancellationToken.None);

        var updatedJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        updatedJoin.ShouldNotBeNull();
        updatedJoin!.CompletedSteps.ShouldBe(2);
        updatedJoin.FailedSteps.ShouldBe(0);
    }

    [Fact]
    public async Task OutboxAck_AutomaticallyReportsJoinCompletion()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);

        var ownerToken = OwnerToken.GenerateNew();

        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId1);

        var joinAfterFirst = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterFirst.ShouldNotBeNull();
        joinAfterFirst!.CompletedSteps.ShouldBe(1);
        joinAfterFirst.FailedSteps.ShouldBe(0);

        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId2);

        var joinAfterSecond = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterSecond.ShouldNotBeNull();
        joinAfterSecond!.CompletedSteps.ShouldBe(2);
        joinAfterSecond.FailedSteps.ShouldBe(0);
    }

    [Fact]
    public async Task OutboxFail_AutomaticallyReportsJoinFailure()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            2,
            null,
            CancellationToken.None);

        var messageId1 = await CreateOutboxMessageAsync();
        var messageId2 = await CreateOutboxMessageAsync();

        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId1, CancellationToken.None);
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId2, CancellationToken.None);

        var ownerToken = OwnerToken.GenerateNew();

        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId1);

        await ClaimMessagesAsync(ownerToken);
        await FailMessageAsync(ownerToken, messageId2);

        var finalJoin = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        finalJoin.ShouldNotBeNull();
        finalJoin!.CompletedSteps.ShouldBe(1);
        finalJoin.FailedSteps.ShouldBe(1);
    }

    [Fact]
    public async Task OutboxAck_MultipleAcksForSameMessage_IsIdempotent()
    {
        var join = await joinStore!.CreateJoinAsync(
            12345,
            1,
            null,
            CancellationToken.None);

        var messageId = await CreateOutboxMessageAsync();
        await joinStore.AttachMessageToJoinAsync(join.JoinId, messageId, CancellationToken.None);

        var ownerToken = OwnerToken.GenerateNew();

        await ClaimMessagesAsync(ownerToken);
        await AckMessageAsync(ownerToken, messageId);

        var joinAfterFirst = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterFirst.ShouldNotBeNull();
        joinAfterFirst!.CompletedSteps.ShouldBe(1);
        joinAfterFirst.FailedSteps.ShouldBe(0);

        await AckMessageAsync(ownerToken, messageId);

        var joinAfterSecond = await joinStore.GetJoinAsync(join.JoinId, CancellationToken.None);
        joinAfterSecond.ShouldNotBeNull();
        joinAfterSecond!.CompletedSteps.ShouldBe(1);
        joinAfterSecond.FailedSteps.ShouldBe(0);
    }

    private async Task ClaimMessagesAsync(OwnerToken ownerToken)
    {
        await outboxService!.ClaimAsync(ownerToken, 30, 10, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task AckMessageAsync(OwnerToken ownerToken, OutboxMessageIdentifier messageId)
    {
        await outboxService!.AckAsync(
            ownerToken,
            new[] { OutboxWorkItemIdentifier.From(messageId.Value) },
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task FailMessageAsync(OwnerToken ownerToken, OutboxMessageIdentifier messageId)
    {
        await outboxService!.FailAsync(
            ownerToken,
            new[] { OutboxWorkItemIdentifier.From(messageId.Value) },
            CancellationToken.None).ConfigureAwait(false);
    }
}
