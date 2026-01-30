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
public class OutboxWorkQueueTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;

    public OutboxWorkQueueTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure work queue schema is set up
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);

        var options = Options.Create(new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            TableName = "Outbox",
        });
        outboxService = new SqlOutboxService(options, NullLogger<SqlOutboxService>.Instance);
    }

    /// <summary>When ready outbox items exist, then ClaimAsync returns their ids.</summary>
    /// <intent>Verify work-queue claims return the ready items.</intent>
    /// <scenario>Given three outbox rows in Ready status and a generated owner token.</scenario>
    /// <behavior>Then ClaimAsync returns three ids from the inserted set.</behavior>
    [Fact]
    public async Task OutboxClaim_WithReadyItems_ReturnsClaimedIds()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(3);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Act
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Assert
        claimedIds.ShouldNotBeEmpty();
        claimedIds.Count.ShouldBe(3);
        claimedIds.ShouldBeSubsetOf(testIds);
    }

    /// <summary>When ClaimAsync is called with a batch size, then it limits the number of claimed ids.</summary>
    /// <intent>Ensure batch size is respected in outbox claims.</intent>
    /// <scenario>Given five ready outbox rows and a batch size of two.</scenario>
    /// <behavior>Then ClaimAsync returns exactly two ids.</behavior>
    [Fact]
    public async Task OutboxClaim_WithBatchSize_RespectsLimit()
    {
        // Arrange
        await CreateTestOutboxItemsAsync(5);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Act
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 2, TestContext.Current.CancellationToken);

        // Assert
        claimedIds.Count.ShouldBe(2);
    }

    /// <summary>When AckAsync is called by the owner, then outbox items are marked Done and processed.</summary>
    /// <intent>Verify successful acknowledgements update outbox status and processed flag.</intent>
    /// <scenario>Given claimed outbox items and the owning token.</scenario>
    /// <behavior>Then the items have Status = Done and IsProcessed = true.</behavior>
    [Fact]
    public async Task OutboxAck_WithValidOwner_MarksDoneAndProcessed()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(2);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Act
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        // Assert
        await VerifyOutboxStatusAsync(claimedIds, 2); // Status = Done
        await VerifyOutboxProcessedAsync(claimedIds, true);
    }

    /// <summary>When AbandonAsync is called by the owner, then outbox items return to Ready status.</summary>
    /// <intent>Ensure abandoned items are made available for reprocessing.</intent>
    /// <scenario>Given claimed outbox items and the owning token.</scenario>
    /// <behavior>Then the items have Status = Ready.</behavior>
    [Fact]
    public async Task OutboxAbandon_WithValidOwner_ReturnsToReady()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(2);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Act
        await outboxService.AbandonAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        // Assert
        await VerifyOutboxStatusAsync(claimedIds, 0); // Status = Ready
    }

    /// <summary>When FailAsync is called by the owner, then outbox items are marked Failed.</summary>
    /// <intent>Verify failure paths update status correctly.</intent>
    /// <scenario>Given claimed outbox items and the owning token.</scenario>
    /// <behavior>Then the items have Status = Failed.</behavior>
    [Fact]
    public async Task OutboxFail_WithValidOwner_MarksAsFailed()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Act
        await outboxService.FailAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        // Assert
        await VerifyOutboxStatusAsync(claimedIds, 3); // Status = Failed
    }

    /// <summary>When leases expire, then ReapExpiredAsync returns items to Ready status.</summary>
    /// <intent>Ensure expired claims are reaped and made available again.</intent>
    /// <scenario>Given a claimed item with a 1-second lease and a delay past expiry.</scenario>
    /// <behavior>Then the item status is reset to Ready after reaping.</behavior>
    [Fact]
    public async Task OutboxReapExpired_WithExpiredItems_ReturnsToReady()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        await outboxService!.ClaimAsync(ownerToken, 1, 10, TestContext.Current.CancellationToken); // 1 second lease

        // Wait for lease to expire
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Act
        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        // Assert
        await VerifyOutboxStatusAsync(testIds, 0); // Status = Ready
    }

    /// <summary>When two workers claim concurrently, then the claimed sets do not overlap.</summary>
    /// <intent>Validate exclusive claiming across concurrent workers.</intent>
    /// <scenario>Given ten ready items and two ClaimAsync calls with different owner tokens.</scenario>
    /// <behavior>Then the combined results have no overlapping ids.</behavior>
    [Fact]
    public async Task ConcurrentClaim_MultipleWorkers_NoOverlap()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(10);
        var worker1Token = OwnerToken.GenerateNew();
        var worker2Token = OwnerToken.GenerateNew();

        // Act - simulate concurrent claims
        var claimTask1 = outboxService!.ClaimAsync(worker1Token, 30, 5, TestContext.Current.CancellationToken);
        var claimTask2 = outboxService.ClaimAsync(worker2Token, 30, 5, TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(claimTask1, claimTask2);
        var claimed1 = results[0];
        var claimed2 = results[1];

        // Assert
        var totalClaimed = claimed1.Count + claimed2.Count;
        totalClaimed.ShouldBeLessThanOrEqualTo(10);

        // No overlap between the two claims
        claimed1.Intersect(claimed2).ShouldBeEmpty();
    }

    /// <summary>When a non-owner tries to acknowledge items, then the items remain InProgress.</summary>
    /// <intent>Ensure owner token enforcement prevents unauthorized updates.</intent>
    /// <scenario>Given claimed items and AckAsync called with a different owner token.</scenario>
    /// <behavior>Then the items remain in Status = InProgress.</behavior>
    [Fact]
    public async Task InvalidOwnerOperations_DoNotAffectItems()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var invalidToken = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Act - try to ack with wrong owner
        await outboxService.AckAsync(invalidToken, claimedIds, TestContext.Current.CancellationToken);

        // Assert - items should still be in claimed state
        await VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    private async Task<List<OutboxWorkItemIdentifier>> CreateTestOutboxItemsAsync(int count)
    {
        var ids = new List<OutboxWorkItemIdentifier>();

        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            for (int i = 0; i < count; i++)
            {
                var id = OutboxWorkItemIdentifier.GenerateNew();
                ids.Add(id);

                await connection.ExecuteAsync(
                    @"
                INSERT INTO infra.Outbox (Id, Topic, Payload, Status, CreatedAt)
                VALUES (@Id, @Topic, @Payload, 0, SYSUTCDATETIME())",
                    new { Id = id, Topic = "test", Payload = $"payload{i}" }).ConfigureAwait(false);
            }

            return ids;
        }
    }

    private async Task VerifyOutboxStatusAsync(IEnumerable<OutboxWorkItemIdentifier> ids, int expectedStatus)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            foreach (var id in ids)
            {
                var status = await connection.ExecuteScalarAsync<int>(
                    "SELECT Status FROM infra.Outbox WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
                status.ShouldBe(expectedStatus);
            }
        }
    }

    private async Task VerifyOutboxProcessedAsync(IEnumerable<OutboxWorkItemIdentifier> ids, bool expectedProcessed)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            foreach (var id in ids)
            {
                var isProcessed = await connection.ExecuteScalarAsync<bool>(
                    "SELECT IsProcessed FROM infra.Outbox WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
                isProcessed.ShouldBe(expectedProcessed);
            }
        }
    }
}

