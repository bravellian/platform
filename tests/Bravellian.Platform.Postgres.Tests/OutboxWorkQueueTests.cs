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
public class OutboxWorkQueueTests : PostgresTestBase
{
    private PostgresOutboxService? outboxService;
    private string qualifiedTableName = string.Empty;

    public OutboxWorkQueueTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);

        var options = Options.Create(new PostgresOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Outbox",
        });
        qualifiedTableName = PostgresSqlHelper.Qualify("infra", "Outbox");
        outboxService = new PostgresOutboxService(options, NullLogger<PostgresOutboxService>.Instance);
    }

    /// <summary>When ready items are available, then ClaimAsync returns their ids.</summary>
    /// <intent>Verify ready outbox work items can be claimed.</intent>
    /// <scenario>Given three ready outbox items and a new owner token.</scenario>
    /// <behavior>ClaimAsync returns all three ids and they match the inserted items.</behavior>
    [Fact]
    public async Task OutboxClaim_WithReadyItems_ReturnsClaimedIds()
    {
        var testIds = await CreateTestOutboxItemsAsync(3);
        var ownerToken = OwnerToken.GenerateNew();

        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        claimedIds.ShouldNotBeEmpty();
        claimedIds.Count.ShouldBe(3);
        claimedIds.ShouldBeSubsetOf(testIds);
    }

    /// <summary>When batch size is smaller than ready items, then ClaimAsync respects the limit.</summary>
    /// <intent>Verify ClaimAsync honors the requested batch size.</intent>
    /// <scenario>Given five ready outbox items and a batch size of two.</scenario>
    /// <behavior>ClaimAsync returns exactly two ids.</behavior>
    [Fact]
    public async Task OutboxClaim_WithBatchSize_RespectsLimit()
    {
        await CreateTestOutboxItemsAsync(5);
        var ownerToken = OwnerToken.GenerateNew();

        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 2, TestContext.Current.CancellationToken);

        claimedIds.Count.ShouldBe(2);
    }

    /// <summary>When acking claimed items with the correct owner, then they are marked done and processed.</summary>
    /// <intent>Verify acknowledgements update status and processing flags.</intent>
    /// <scenario>Given claimed items and the matching owner token.</scenario>
    /// <behavior>Status becomes Done and IsProcessed is true for each claimed item.</behavior>
    [Fact]
    public async Task OutboxAck_WithValidOwner_MarksDoneAndProcessed()
    {
        var testIds = await CreateTestOutboxItemsAsync(2);
        var ownerToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.Done);
        await VerifyOutboxProcessedAsync(claimedIds, true);
    }

    /// <summary>When abandoning claimed items with the correct owner, then they return to Ready.</summary>
    /// <intent>Verify abandon resets status for claimed items.</intent>
    /// <scenario>Given claimed items and the matching owner token.</scenario>
    /// <behavior>Status returns to Ready for each claimed item.</behavior>
    [Fact]
    public async Task OutboxAbandon_WithValidOwner_ReturnsToReady()
    {
        var testIds = await CreateTestOutboxItemsAsync(2);
        var ownerToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.AbandonAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.Ready);
    }

    /// <summary>When failing claimed items with the correct owner, then they are marked Failed.</summary>
    /// <intent>Verify failure marks the work items as failed.</intent>
    /// <scenario>Given claimed items and the matching owner token.</scenario>
    /// <behavior>Status becomes Failed for each claimed item.</behavior>
    [Fact]
    public async Task OutboxFail_WithValidOwner_MarksAsFailed()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var ownerToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.FailAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.Failed);
    }

    /// <summary>When a claim expires, then ReapExpired returns items to Ready.</summary>
    /// <intent>Verify expired leases are reaped back to Ready.</intent>
    /// <scenario>Given a claimed item with a 1-second lease that is allowed to expire.</scenario>
    /// <behavior>ReapExpiredAsync resets the item status to Ready.</behavior>
    [Fact]
    public async Task OutboxReapExpired_WithExpiredItems_ReturnsToReady()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var ownerToken = OwnerToken.GenerateNew();
        await outboxService!.ClaimAsync(ownerToken, 1, 10, TestContext.Current.CancellationToken);

        await Task.Delay(1500, TestContext.Current.CancellationToken);

        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(testIds, OutboxStatus.Ready);
    }

    /// <summary>When two workers claim concurrently, then claimed sets do not overlap.</summary>
    /// <intent>Verify concurrent claims are exclusive per work item.</intent>
    /// <scenario>Given ten ready items and two workers claiming up to five each.</scenario>
    /// <behavior>Claimed ids are disjoint and total claimed does not exceed ten.</behavior>
    [Fact]
    public async Task ConcurrentClaim_MultipleWorkers_NoOverlap()
    {
        var testIds = await CreateTestOutboxItemsAsync(10);
        var worker1Token = OwnerToken.GenerateNew();
        var worker2Token = OwnerToken.GenerateNew();

        var claimTask1 = outboxService!.ClaimAsync(worker1Token, 30, 5, TestContext.Current.CancellationToken);
        var claimTask2 = outboxService.ClaimAsync(worker2Token, 30, 5, TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(claimTask1, claimTask2);
        var claimed1 = results[0];
        var claimed2 = results[1];

        var totalClaimed = claimed1.Count + claimed2.Count;
        totalClaimed.ShouldBeLessThanOrEqualTo(10);

        claimed1.Intersect(claimed2).ShouldBeEmpty();
    }

    /// <summary>When a non-owner attempts to ack items, then item status is unchanged.</summary>
    /// <intent>Verify owner token enforcement for state changes.</intent>
    /// <scenario>Given claimed items owned by one token and an ack attempt with another token.</scenario>
    /// <behavior>Status remains InProgress for the claimed items.</behavior>
    [Fact]
    public async Task InvalidOwnerOperations_DoNotAffectItems()
    {
        var testIds = await CreateTestOutboxItemsAsync(1);
        var ownerToken = OwnerToken.GenerateNew();
        var invalidToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        await outboxService.AckAsync(invalidToken, claimedIds, TestContext.Current.CancellationToken);

        await VerifyOutboxStatusAsync(claimedIds, OutboxStatus.InProgress);
    }

    private async Task<List<OutboxWorkItemIdentifier>> CreateTestOutboxItemsAsync(int count)
    {
        var ids = new List<OutboxWorkItemIdentifier>();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        for (int i = 0; i < count; i++)
        {
            var id = OutboxWorkItemIdentifier.GenerateNew();
            ids.Add(id);

            await connection.ExecuteAsync(
                $"""
                INSERT INTO {qualifiedTableName} ("Id", "Topic", "Payload", "Status", "CreatedAt", "MessageId")
                VALUES (@Id, @Topic, @Payload, 0, CURRENT_TIMESTAMP, @MessageId)
                """,
                new { Id = id, Topic = "test", Payload = $"payload{i}", MessageId = Guid.NewGuid() }).ConfigureAwait(false);
        }

        return ids;
    }

    private async Task VerifyOutboxStatusAsync(IEnumerable<OutboxWorkItemIdentifier> ids, byte expectedStatus)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (var id in ids)
        {
            var status = await connection.ExecuteScalarAsync<short>(
                $"SELECT \"Status\" FROM {qualifiedTableName} WHERE \"Id\" = @Id", new { Id = id }).ConfigureAwait(false);
            ((byte)status).ShouldBe(expectedStatus);
        }
    }

    private async Task VerifyOutboxProcessedAsync(IEnumerable<OutboxWorkItemIdentifier> ids, bool expectedProcessed)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (var id in ids)
        {
            var isProcessed = await connection.ExecuteScalarAsync<bool>(
                $"SELECT \"IsProcessed\" FROM {qualifiedTableName} WHERE \"Id\" = @Id", new { Id = id }).ConfigureAwait(false);
            isProcessed.ShouldBe(expectedProcessed);
        }
    }
}

