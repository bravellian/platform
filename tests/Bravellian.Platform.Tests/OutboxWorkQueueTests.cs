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

namespace Bravellian.Platform.Tests;

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

public class OutboxWorkQueueTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;

    public OutboxWorkQueueTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure work queue schema is set up
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.ConnectionString).ConfigureAwait(false);

        var options = Options.Create(new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox",
        });
        this.outboxService = new SqlOutboxService(options, NullLogger<SqlOutboxService>.Instance);
    }

    [Fact]
    public async Task OutboxClaim_WithReadyItems_ReturnsClaimedIds()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(3);
        var ownerToken = Guid.NewGuid();

        // Act
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Assert
        claimedIds.ShouldNotBeEmpty();
        claimedIds.Count.ShouldBe(3);
        claimedIds.ShouldBeSubsetOf(testIds);
    }

    [Fact]
    public async Task OutboxClaim_WithBatchSize_RespectsLimit()
    {
        // Arrange
        await this.CreateTestOutboxItemsAsync(5);
        var ownerToken = Guid.NewGuid();

        // Act
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 2);

        // Assert
        claimedIds.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OutboxAck_WithValidOwner_MarksDoneAndProcessed()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(2);
        var ownerToken = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Act
        await this.outboxService.AckAsync(ownerToken, claimedIds);

        // Assert
        await this.VerifyOutboxStatusAsync(claimedIds, 2); // Status = Done
        await this.VerifyOutboxProcessedAsync(claimedIds, true);
    }

    [Fact]
    public async Task OutboxAbandon_WithValidOwner_ReturnsToReady()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(2);
        var ownerToken = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Act
        await this.outboxService.AbandonAsync(ownerToken, claimedIds);

        // Assert
        await this.VerifyOutboxStatusAsync(claimedIds, 0); // Status = Ready
    }

    [Fact]
    public async Task OutboxFail_WithValidOwner_MarksAsFailed()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var ownerToken = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Act
        await this.outboxService.FailAsync(ownerToken, claimedIds);

        // Assert
        await this.VerifyOutboxStatusAsync(claimedIds, 3); // Status = Failed
    }

    [Fact]
    public async Task OutboxReapExpired_WithExpiredItems_ReturnsToReady()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var ownerToken = Guid.NewGuid();
        await this.outboxService!.ClaimAsync(ownerToken, 1, 10); // 1 second lease

        // Wait for lease to expire
        await Task.Delay(1500);

        // Act
        await this.outboxService.ReapExpiredAsync();

        // Assert
        await this.VerifyOutboxStatusAsync(testIds, 0); // Status = Ready
    }

    [Fact]
    public async Task ConcurrentClaim_MultipleWorkers_NoOverlap()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(10);
        var worker1Token = Guid.NewGuid();
        var worker2Token = Guid.NewGuid();

        // Act - simulate concurrent claims
        var claimTask1 = this.outboxService!.ClaimAsync(worker1Token, 30, 5);
        var claimTask2 = this.outboxService.ClaimAsync(worker2Token, 30, 5);

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
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var ownerToken = Guid.NewGuid();
        var invalidToken = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Act - try to ack with wrong owner
        await this.outboxService.AckAsync(invalidToken, claimedIds);

        // Assert - items should still be in claimed state
        await this.VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    private async Task<List<Guid>> CreateTestOutboxItemsAsync(int count)
    {
        var ids = new List<Guid>();

        var connection = new SqlConnection(this.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
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

    private async Task VerifyOutboxStatusAsync(IEnumerable<Guid> ids, int expectedStatus)
    {
        var connection = new SqlConnection(this.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

        foreach (var id in ids)
        {
            var status = await connection.ExecuteScalarAsync<int>(
                "SELECT Status FROM dbo.Outbox WHERE Id = @Id", new { Id = id });
            status.ShouldBe(expectedStatus);
        }
        }
    }

    private async Task VerifyOutboxProcessedAsync(IEnumerable<Guid> ids, bool expectedProcessed)
    {
        var connection = new SqlConnection(this.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

        foreach (var id in ids)
        {
            var isProcessed = await connection.ExecuteScalarAsync<bool>(
                "SELECT IsProcessed FROM dbo.Outbox WHERE Id = @Id", new { Id = id });
            isProcessed.ShouldBe(expectedProcessed);
        }
        }
    }
}
