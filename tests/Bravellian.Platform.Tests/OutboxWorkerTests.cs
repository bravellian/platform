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
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxWorkerTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;
    private TestOutboxWorker? worker;

    public OutboxWorkerTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Test connection with retry logic for CI stability
        await WaitForDatabaseReadyAsync(ConnectionString).ConfigureAwait(false);

        // Ensure work queue schema is set up
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);

        var options = Options.Create(new SqlOutboxOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox",
        });
        outboxService = new SqlOutboxService(options, new TestLogger<SqlOutboxService>(TestOutputHelper));
        worker = new TestOutboxWorker(outboxService, new TestLogger<TestOutboxWorker>(TestOutputHelper));
    }

    private async Task WaitForDatabaseReadyAsync(string connectionString)
    {
        const int maxRetries = 10;
        const int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var connection = new SqlConnection(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    await connection.OpenAsync(TestContext.Current.CancellationToken);
                    await using var command = new SqlCommand("SELECT 1", connection);
                    await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
                    return; // Success
                }
            }
            catch (Exception ex)
            {
                TestOutputHelper.WriteLine($"Database connection attempt {i + 1} failed: {ex.Message}");
                if (i == maxRetries - 1)
                {
                    throw new InvalidOperationException($"Database not ready after {maxRetries} attempts", ex);
                }

                await Task.Delay(delayMs, TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
        }
    }

    [Fact]
    public async Task Worker_ProcessesClaimedItems_AndAcknowledgesThem()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(3);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker!.StartAsync(cts.Token);

        // Give the worker time to process items
        await Task.Delay(1000, cts.Token);
        await worker.StopAsync(cts.Token);

        // Assert
        worker.ProcessedItems.Count.ShouldBe(3);
        worker.ProcessedItems.ShouldBeSubsetOf(testIds);

        // Verify items are marked as processed in database
        await VerifyOutboxStatusAsync(testIds, 2); // Status = Done
    }

    [Fact]
    public async Task Worker_WithProcessingFailure_AbandonsItems()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(2);
        worker!.ShouldFailProcessing = true;
        worker.ProcessingDelay = TimeSpan.FromMilliseconds(50); // Shorter delay for testing
        worker.RunOnce = true;

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await worker.StartAsync(cts.Token);

        // Give the worker time to claim and attempt processing
        await Task.Delay(3000, cts.Token);
        await worker.StopAsync(cts.Token);

        // Assert
        // Items should be abandoned and back to ready state
        await VerifyOutboxStatusAsync(testIds, 0); // Status = Ready
    }

    [Fact]
    public async Task Worker_ClaimsItemsCorrectly()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(2);

        // Act - claim items manually to test the claim operation
        var claimedIds = await outboxService!.ClaimAsync(OwnerToken.GenerateNew(), 30, 10, TestContext.Current.CancellationToken);

        // Assert
        claimedIds.Count.ShouldBe(2);
        claimedIds.ShouldBeSubsetOf(testIds);

        // Verify items are now in InProgress state
        await VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    [Fact]
    public async Task Manual_AbandonOperation_Works()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(2);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Act
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);
        await VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress

        await outboxService.AbandonAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);

        // Assert
        await VerifyOutboxStatusAsync(claimedIds, 0); // Status = Ready
    }

    [Fact]
    public async Task WorkQueue_LeaseExpiration_AllowsReclaim()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();

        // Act - first owner claims with short lease
        var claimed1 = await outboxService!.ClaimAsync(owner1, 1, 10, TestContext.Current.CancellationToken); // 1 second lease
        claimed1.Count.ShouldBe(1);

        // Wait for lease to expire
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Reap expired items
        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        // Second owner should be able to claim the same item
        var claimed2 = await outboxService.ClaimAsync(owner2, 30, 10, TestContext.Current.CancellationToken);

        // Assert
        claimed2.Count.ShouldBe(1);
        claimed2[0].ShouldBe(claimed1[0]); // Same item
    }

    [Fact]
    public async Task WorkQueue_RestartUsesNewOwnerTokenAfterReap()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        var firstOwner = OwnerToken.GenerateNew();
        var secondOwner = OwnerToken.GenerateNew();

        // Act - claim with first owner and let lease expire
        var claimed1 = await outboxService!.ClaimAsync(firstOwner, 1, 1, TestContext.Current.CancellationToken);
        claimed1.ShouldHaveSingleItem();

        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await outboxService.ReapExpiredAsync(TestContext.Current.CancellationToken);

        var claimed2 = await outboxService.ClaimAsync(secondOwner, 30, 1, TestContext.Current.CancellationToken);

        // Assert
        claimed2.ShouldHaveSingleItem();
        await VerifyOwnerTokenAsync(claimed1[0], firstOwner.Value);
        await VerifyOwnerTokenAsync(claimed2[0], secondOwner.Value);
        claimed1[0].ShouldBe(claimed2[0]);
    }

    [Fact]
    public async Task WorkQueue_IdempotentOperations_NoErrors()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(ownerToken, 30, 10, TestContext.Current.CancellationToken);

        // Act - multiple acks should be harmless
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken);
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken); // Second ack
        await outboxService.AckAsync(ownerToken, claimedIds, TestContext.Current.CancellationToken); // Third ack

        // Assert - should remain acknowledged
        await VerifyOutboxStatusAsync(claimedIds, 2); // Status = Done
    }

    [Fact]
    public async Task WorkQueue_UnauthorizedOwner_CannotModify()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(1);
        var owner1 = OwnerToken.GenerateNew();
        var owner2 = OwnerToken.GenerateNew();
        var claimedIds = await outboxService!.ClaimAsync(owner1, 30, 10, TestContext.Current.CancellationToken);

        // Act - different owner tries to ack
        await outboxService.AckAsync(owner2, claimedIds, TestContext.Current.CancellationToken);

        // Assert - item should still be claimed by original owner
        await VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    [Fact]
    public async Task WorkQueue_EmptyIdLists_NoErrors()
    {
        // Arrange
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
        var emptyIds = new List<OutboxWorkItemIdentifier>();

        // Act & Assert - should not throw
        await outboxService!.AckAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
        await outboxService.AbandonAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
        await outboxService.FailAsync(ownerToken, emptyIds, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WorkQueue_ConcurrentClaims_NoOverlap()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(10);
        var tasks = new List<Task<IReadOnlyList<OutboxWorkItemIdentifier>>>();

        // Act - multiple workers claim simultaneously
        for (int i = 0; i < 5; i++)
        {
            Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();
            tasks.Add(outboxService!.ClaimAsync(ownerToken, 30, 3, TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - no item should be claimed by multiple workers
        var allClaimed = results.SelectMany(r => r).ToList();
        var uniqueClaimed = allClaimed.Distinct().ToList();

        allClaimed.Count.ShouldBe(uniqueClaimed.Count); // No duplicates
        uniqueClaimed.Count.ShouldBeLessThanOrEqualTo(10); // Can't claim more than available
    }

    [Fact]
    public async Task Worker_RespectsCancellationToken()
    {
        // Arrange
        var testIds = await CreateTestOutboxItemsAsync(5);
        worker!.ProcessingDelay = TimeSpan.FromSeconds(10); // Long delay

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StartAsync(cts.Token);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(cts.Token);
        stopwatch.Stop();

        // Assert
        // Worker should stop quickly due to cancellation
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
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
                    "SELECT Status FROM dbo.Outbox WHERE Id = @Id", new { Id = id.Value });
                status.ShouldBe(expectedStatus);
            }
        }
    }

    private async Task VerifyOwnerTokenAsync(OutboxWorkItemIdentifier id, Guid expectedOwner)
    {
        var connection = new SqlConnection(ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var ownerToken = await connection.ExecuteScalarAsync<Guid?>("SELECT OwnerToken FROM dbo.Outbox WHERE Id = @Id", new { Id = id.Value });
            ownerToken.ShouldBe(expectedOwner);
        }
    }

    private class TestOutboxWorker : BackgroundService
    {
        private readonly IOutbox outbox;
        private readonly ILogger<TestOutboxWorker> logger;
        private readonly Bravellian.Platform.OwnerToken ownerToken = OwnerToken.GenerateNew();

        public TestOutboxWorker(IOutbox outbox, ILogger<TestOutboxWorker> logger)
        {
            this.outbox = outbox;
            this.logger = logger;
        }

        public List<OutboxWorkItemIdentifier> ProcessedItems { get; } = new();

        public bool ShouldFailProcessing { get; set; }

        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        public bool RunOnce { get; set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var claimedIds = await outbox.ClaimAsync(ownerToken, 30, 10, stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("Worker claimed {Count} items", claimedIds.Count);

                    if (claimedIds.Count == 0)
                    {
                        if (RunOnce)
                        {
                            break;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var succeededIds = new List<OutboxWorkItemIdentifier>();
                    var failedIds = new List<OutboxWorkItemIdentifier>();

                    foreach (var id in claimedIds)
                    {
                        try
                        {
                            await Task.Delay(ProcessingDelay, stoppingToken).ConfigureAwait(false);

                            if (ShouldFailProcessing)
                            {
                                logger.LogInformation("Simulating failure for item {Id}", id);
                                throw new InvalidOperationException("Simulated processing failure");
                            }

                            ProcessedItems.Add(id);
                            succeededIds.Add(id);
                            logger.LogInformation("Successfully processed item {Id}", id);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to process outbox item {Id}", id);
                            failedIds.Add(id);
                        }
                    }

                    if (succeededIds.Count > 0)
                    {
                        logger.LogInformation("Acknowledging {Count} successful items", succeededIds.Count);
                        await outbox.AckAsync(ownerToken, succeededIds, stoppingToken).ConfigureAwait(false);
                    }

                    if (failedIds.Count > 0)
                    {
                        logger.LogInformation("Abandoning {Count} failed items", failedIds.Count);
                        await outbox.AbandonAsync(ownerToken, failedIds, stoppingToken).ConfigureAwait(false);
                    }

                    if (RunOnce)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Worker cancelled due to stopping token");
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in outbox processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }
        }
    }
}
