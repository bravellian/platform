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

    /// <summary>When CreateJoinAsync is called with valid parameters, then a pending join is created.</summary>
    /// <intent>Verify join creation populates expected metadata and counters.</intent>
    /// <scenario>Given a tenant id, expected steps, and metadata payload.</scenario>
    /// <behavior>The join has a non-empty id, pending status, zero counts, and a recent CreatedUtc.</behavior>
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

    /// <summary>When retrieving an existing join, then GetJoinAsync returns it.</summary>
    /// <intent>Verify joins can be fetched by id.</intent>
    /// <scenario>Given a join created via CreateJoinAsync.</scenario>
    /// <behavior>The retrieved join matches the created JoinId and ExpectedSteps.</behavior>
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

    /// <summary>When retrieving a non-existent join, then GetJoinAsync returns null.</summary>
    /// <intent>Verify unknown join ids are handled gracefully.</intent>
    /// <scenario>Given a randomly generated JoinIdentifier.</scenario>
    /// <behavior>The retrieved join is null.</behavior>
    [Fact]
    public async Task GetJoinAsync_WithNonExistentJoin_ReturnsNull()
    {
        var nonExistentJoinId = JoinIdentifier.GenerateNew();

        var join = await joinStore!.GetJoinAsync(
            nonExistentJoinId,
            CancellationToken.None);

        join.ShouldBeNull();
    }

    /// <summary>When attaching a message to a join, then the association is stored.</summary>
    /// <intent>Verify join member rows are created for attached messages.</intent>
    /// <scenario>Given an existing join and a created outbox message id.</scenario>
    /// <behavior>The join member table has one row for the join/message pair.</behavior>
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

    /// <summary>When attaching the same message twice, then only one association exists.</summary>
    /// <intent>Verify AttachMessageToJoinAsync is idempotent.</intent>
    /// <scenario>Given a join and a single outbox message attached twice.</scenario>
    /// <behavior>The join member table contains a single row for that pair.</behavior>
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

    /// <summary>When incrementing completion for an attached message, then CompletedSteps increases.</summary>
    /// <intent>Verify completion updates counters and timestamps.</intent>
    /// <scenario>Given a join with an attached message.</scenario>
    /// <behavior>CompletedSteps is 1, FailedSteps is 0, and LastUpdatedUtc advances.</behavior>
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

    /// <summary>When incrementing failure for an attached message, then FailedSteps increases.</summary>
    /// <intent>Verify failure updates counters and timestamps.</intent>
    /// <scenario>Given a join with an attached message.</scenario>
    /// <behavior>FailedSteps is 1, CompletedSteps is 0, and LastUpdatedUtc advances.</behavior>
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

    /// <summary>When UpdateStatusAsync sets a status, then the join status is updated.</summary>
    /// <intent>Verify status updates persist for joins.</intent>
    /// <scenario>Given a pending join that is updated to Completed.</scenario>
    /// <behavior>The stored join status is Completed.</behavior>
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

    /// <summary>When a join has multiple attached messages, then GetJoinMessagesAsync returns all ids.</summary>
    /// <intent>Verify join message enumeration returns every attached message.</intent>
    /// <scenario>Given a join with three attached outbox messages.</scenario>
    /// <behavior>The returned list contains all three message ids.</behavior>
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

    /// <summary>When all steps complete, then the join reports completed status and counts.</summary>
    /// <intent>Verify the full join workflow records completion for every step.</intent>
    /// <scenario>Given a join expecting three steps with three attached messages all completed.</scenario>
    /// <behavior>CompletedSteps is 3, FailedSteps is 0, and Status is Completed.</behavior>
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

    /// <summary>When some steps fail, then the join reports failed status and counts.</summary>
    /// <intent>Verify the join workflow records failures alongside completions.</intent>
    /// <scenario>Given a join expecting three steps with two completed and one failed.</scenario>
    /// <behavior>Status is Failed with CompletedSteps 2 and FailedSteps 1.</behavior>
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

    /// <summary>When IncrementCompletedAsync is called twice for the same message, then counts are not duplicated.</summary>
    /// <intent>Verify completion increments are idempotent per message.</intent>
    /// <scenario>Given a join with one attached message completed twice.</scenario>
    /// <behavior>CompletedSteps remains 1 and FailedSteps remains 0.</behavior>
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

    /// <summary>When IncrementFailedAsync is called twice for the same message, then counts are not duplicated.</summary>
    /// <intent>Verify failure increments are idempotent per message.</intent>
    /// <scenario>Given a join with one attached message failed twice.</scenario>
    /// <behavior>FailedSteps remains 1 and CompletedSteps remains 0.</behavior>
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

    /// <summary>When completed steps would exceed expected, then counts do not overrun expected steps.</summary>
    /// <intent>Verify completion counting is capped at ExpectedSteps.</intent>
    /// <scenario>Given a join expecting two steps with three attached messages completed.</scenario>
    /// <behavior>CompletedSteps remains at 2 and FailedSteps remains 0.</behavior>
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

    /// <summary>When outbox messages are acked, then join completion counts are incremented.</summary>
    /// <intent>Verify outbox acknowledgements report join completion automatically.</intent>
    /// <scenario>Given a join expecting two messages attached to the outbox.</scenario>
    /// <behavior>CompletedSteps moves from 1 after the first ack to 2 after the second.</behavior>
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

    /// <summary>When outbox messages fail, then join failure counts are incremented.</summary>
    /// <intent>Verify outbox failures report join failures automatically.</intent>
    /// <scenario>Given a join expecting two messages where one is acked and one fails.</scenario>
    /// <behavior>CompletedSteps is 1 and FailedSteps is 1.</behavior>
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

    /// <summary>When the same message is acked multiple times, then join counts are not over-incremented.</summary>
    /// <intent>Verify outbox ack reporting is idempotent per message.</intent>
    /// <scenario>Given a join expecting one message that is acked twice.</scenario>
    /// <behavior>CompletedSteps remains 1 and FailedSteps remains 0.</behavior>
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
