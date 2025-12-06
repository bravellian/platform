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
            SchemaName = "dbo",
            TableName = "Outbox",
        });
        outboxService = new SqlOutboxService(options, NullLogger<SqlOutboxService>.Instance);
    }

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
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            for (int i = 0; i < count; i++)
            {
                var id = OutboxWorkItemIdentifier.GenerateNew();
                ids.Add(id);

                await connection.ExecuteAsync(
                    @"
                INSERT INTO dbo.Outbox (Id, Topic, Payload, Status, CreatedAt)
                VALUES (@Id, @Topic, @Payload, 0, SYSUTCDATETIME())",
                    new { Id = id, Topic = "test", Payload = $"payload{i}" });
            }

            return ids;
        }
    }

    private async Task VerifyOutboxStatusAsync(IEnumerable<OutboxWorkItemIdentifier> ids, int expectedStatus)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            foreach (var id in ids)
            {
                var status = await connection.ExecuteScalarAsync<int>(
                    "SELECT Status FROM dbo.Outbox WHERE Id = @Id", new { Id = id });
                status.ShouldBe(expectedStatus);
            }
        }
    }

    private async Task VerifyOutboxProcessedAsync(IEnumerable<OutboxWorkItemIdentifier> ids, bool expectedProcessed)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            foreach (var id in ids)
            {
                var isProcessed = await connection.ExecuteScalarAsync<bool>(
                    "SELECT IsProcessed FROM dbo.Outbox WHERE Id = @Id", new { Id = id });
                isProcessed.ShouldBe(expectedProcessed);
            }
        }
    }
}
