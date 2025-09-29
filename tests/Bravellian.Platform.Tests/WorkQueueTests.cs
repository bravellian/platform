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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

/// <summary>
/// Tests for work queue functionality including concurrency, lease expiration, and idempotency.
/// </summary>
public class WorkQueueTests : SqlServerTestBase
{
    private IOutboxWorkQueue? outboxWorkQueue;
    private ITimerWorkQueue? timerWorkQueue;

    protected override async Task SetupAsync()
    {
        await base.SetupAsync();
        
        // Setup work queue schemas
        await WorkQueueSchemaManager.EnsureWorkQueueSchemaAsync(
            this.ConnectionString, "dbo", "Outbox", "Id", "UNIQUEIDENTIFIER", "CreatedAt");
        
        await WorkQueueSchemaManager.EnsureScheduledWorkQueueSchemaAsync(
            this.ConnectionString, "dbo", "Timers", "Id", "UNIQUEIDENTIFIER", "DueTime");

        this.outboxWorkQueue = new SqlOutboxWorkQueue(new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        });

        this.timerWorkQueue = new SqlTimerWorkQueue(this.ConnectionString, "dbo", "Timers");
    }

    [Fact]
    public async Task OutboxWorkQueue_ClaimAsync_WithNoItems_ReturnsEmpty()
    {
        // Arrange
        var ownerToken = Guid.NewGuid();

        // Act
        var result = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task OutboxWorkQueue_ClaimAsync_WithAvailableItems_ReturnsItems()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(3);
        var ownerToken = Guid.NewGuid();

        // Act
        var result = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldBeSubsetOf(outboxIds);
    }

    [Fact]
    public async Task OutboxWorkQueue_ClaimAsync_RespectsBatchSize()
    {
        // Arrange
        await CreateTestOutboxMessages(5);
        var ownerToken = Guid.NewGuid();

        // Act
        var result = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 2);

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OutboxWorkQueue_AckAsync_MarksItemsAsCompleted()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(3);
        var ownerToken = Guid.NewGuid();
        
        var claimed = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);
        
        // Act
        await this.outboxWorkQueue.AckAsync(ownerToken, claimed);

        // Assert
        var statuses = await GetOutboxStatuses(claimed);
        statuses.ShouldAllBe(status => status == WorkQueueStatus.Done);
    }

    [Fact]
    public async Task OutboxWorkQueue_AbandonAsync_ReturnsItemsToReady()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(3);
        var ownerToken = Guid.NewGuid();
        
        var claimed = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);
        
        // Act
        await this.outboxWorkQueue.AbandonAsync(ownerToken, claimed);

        // Assert
        var statuses = await GetOutboxStatuses(claimed);
        statuses.ShouldAllBe(status => status == WorkQueueStatus.Ready);
        
        // Should be claimable by another worker
        var anotherOwner = Guid.NewGuid();
        var reclaimedIds = await this.outboxWorkQueue.ClaimAsync(anotherOwner, 30, 10);
        reclaimedIds.Count.ShouldBe(3);
    }

    [Fact]
    public async Task OutboxWorkQueue_FailAsync_MarksItemsAsFailed()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(2);
        var ownerToken = Guid.NewGuid();
        
        var claimed = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);
        
        // Act
        await this.outboxWorkQueue.FailAsync(ownerToken, claimed, "Test error message");

        // Assert
        var statuses = await GetOutboxStatuses(claimed);
        statuses.ShouldAllBe(status => status == WorkQueueStatus.Failed);
    }

    [Fact]
    public async Task OutboxWorkQueue_ConcurrentWorkers_NoDoubleProcessing()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(10);
        var worker1Token = Guid.NewGuid();
        var worker2Token = Guid.NewGuid();

        // Act - Two workers claim simultaneously
        var task1 = this.outboxWorkQueue!.ClaimAsync(worker1Token, 30, 10);
        var task2 = this.outboxWorkQueue.ClaimAsync(worker2Token, 30, 10);
        
        var results = await Task.WhenAll(task1, task2);
        var worker1Items = results[0];
        var worker2Items = results[1];

        // Assert
        var totalClaimed = worker1Items.Count + worker2Items.Count;
        totalClaimed.ShouldBe(10); // All items should be claimed exactly once
        
        // No overlap in claimed items
        worker1Items.Intersect(worker2Items).ShouldBeEmpty();
    }

    [Fact]
    public async Task OutboxWorkQueue_LeaseExpiration_AllowsReclaim()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(2);
        var ownerToken = Guid.NewGuid();
        
        // Claim with very short lease
        var claimed = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 1, 10);
        claimed.Count.ShouldBe(2);

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act - Another worker should be able to claim the expired items
        var anotherOwner = Guid.NewGuid();
        var reclaimedIds = await this.outboxWorkQueue.ClaimAsync(anotherOwner, 30, 10);

        // Assert
        reclaimedIds.Count.ShouldBe(2);
        reclaimedIds.ShouldBeSubsetOf(claimed);
    }

    [Fact]
    public async Task OutboxWorkQueue_ReapExpiredAsync_ReturnsExpiredItems()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(3);
        var ownerToken = Guid.NewGuid();
        
        // Claim with very short lease
        await this.outboxWorkQueue!.ClaimAsync(ownerToken, 1, 10);
        
        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act
        var reapedCount = await this.outboxWorkQueue.ReapExpiredAsync();

        // Assert
        reapedCount.ShouldBe(3);
        
        // Items should be available for claiming again
        var anotherOwner = Guid.NewGuid();
        var availableItems = await this.outboxWorkQueue.ClaimAsync(anotherOwner, 30, 10);
        availableItems.Count.ShouldBe(3);
    }

    [Fact]
    public async Task OutboxWorkQueue_IdempotentOperations_NoSideEffects()
    {
        // Arrange
        var outboxIds = await CreateTestOutboxMessages(2);
        var ownerToken = Guid.NewGuid();
        var claimed = await this.outboxWorkQueue!.ClaimAsync(ownerToken, 30, 10);

        // Act - Multiple Ack calls should be harmless
        await this.outboxWorkQueue.AckAsync(ownerToken, claimed);
        await this.outboxWorkQueue.AckAsync(ownerToken, claimed); // Second call
        await this.outboxWorkQueue.AckAsync(ownerToken, claimed); // Third call

        // Assert - Items should still be marked as done
        var statuses = await GetOutboxStatuses(claimed);
        statuses.ShouldAllBe(status => status == WorkQueueStatus.Done);
    }

    [Fact]
    public async Task TimerWorkQueue_ClaimDue_OnlyReturnsDueItems()
    {
        // Arrange
        var futureTime = DateTimeOffset.UtcNow.AddHours(1);
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        
        var futureTimerId = await CreateTestTimer(futureTime);
        var pastTimerId = await CreateTestTimer(pastTime);
        
        var ownerToken = Guid.NewGuid();

        // Act
        var claimed = await this.timerWorkQueue!.ClaimAsync(ownerToken, 30, 10);

        // Assert
        claimed.Count.ShouldBe(1);
        claimed.ShouldContain(pastTimerId);
        claimed.ShouldNotContain(futureTimerId);
    }

    [Fact]
    public async Task TimerWorkQueue_AllOperationsWork()
    {
        // Arrange
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        var timerId = await CreateTestTimer(pastTime);
        var ownerToken = Guid.NewGuid();

        // Act & Assert - Claim
        var claimed = await this.timerWorkQueue!.ClaimAsync(ownerToken, 30, 10);
        claimed.Count.ShouldBe(1);
        claimed.ShouldContain(timerId);

        // Act & Assert - Ack
        await this.timerWorkQueue.AckAsync(ownerToken, claimed);
        var status = await GetTimerStatus(timerId);
        status.ShouldBe(WorkQueueStatus.Done);
    }

    private async Task<List<Guid>> CreateTestOutboxMessages(int count)
    {
        const string sql = @"
            INSERT INTO dbo.Outbox (Topic, Payload, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Topic, @Payload, SYSDATETIMEOFFSET())";

        var ids = new List<Guid>();
        
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        for (int i = 0; i < count; i++)
        {
            var id = await connection.QuerySingleAsync<Guid>(sql, new
            {
                Topic = $"test-topic-{i}",
                Payload = $"test-payload-{i}"
            });
            ids.Add(id);
        }

        return ids;
    }

    private async Task<Guid> CreateTestTimer(DateTimeOffset dueTime)
    {
        const string sql = @"
            INSERT INTO dbo.Timers (Topic, Payload, DueTime, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@Topic, @Payload, @DueTime, SYSDATETIMEOFFSET())";

        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        return await connection.QuerySingleAsync<Guid>(sql, new
        {
            Topic = "test-timer-topic",
            Payload = "test-timer-payload",
            DueTime = dueTime
        });
    }

    private async Task<List<byte>> GetOutboxStatuses(IEnumerable<Guid> ids)
    {
        const string sql = "SELECT Status FROM dbo.Outbox WHERE Id IN @Ids";
        
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        var statuses = await connection.QueryAsync<byte>(sql, new { Ids = ids });
        return statuses.ToList();
    }

    private async Task<byte> GetTimerStatus(Guid id)
    {
        const string sql = "SELECT Status FROM dbo.Timers WHERE Id = @Id";
        
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        return await connection.QuerySingleAsync<byte>(sql, new { Id = id });
    }
}