namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Shouldly;

public class OutboxWorkerTests : SqlServerTestBase
{
    private SqlOutboxService? outboxService;
    private TestOutboxWorker? worker;

    public OutboxWorkerTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Test connection with retry logic for CI stability
        await WaitForDatabaseReadyAsync(this.ConnectionString);

        // Ensure work queue schema is set up
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.ConnectionString);

        var options = Options.Create(new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox",
        });
        this.outboxService = new SqlOutboxService(options, new TestLogger<SqlOutboxService>(this.TestOutputHelper));
        this.worker = new TestOutboxWorker(this.outboxService, new TestLogger<TestOutboxWorker>(this.TestOutputHelper));
    }

    private async Task WaitForDatabaseReadyAsync(string connectionString)
    {
        const int maxRetries = 10;
        const int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync();
                return; // Success
            }
            catch (Exception ex)
            {
                this.TestOutputHelper.WriteLine($"Database connection attempt {i + 1} failed: {ex.Message}");
                if (i == maxRetries - 1)
                    throw new InvalidOperationException($"Database not ready after {maxRetries} attempts", ex);
                await Task.Delay(delayMs);
            }
        }
    }

    [Fact]
    public async Task Worker_ProcessesClaimedItems_AndAcknowledgesThem()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(3);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await this.worker!.StartAsync(cts.Token);

        // Give the worker time to process items
        await Task.Delay(1000, cts.Token);
        await this.worker.StopAsync(cts.Token);

        // Assert
        this.worker.ProcessedItems.Count.ShouldBe(3);
        this.worker.ProcessedItems.ShouldBeSubsetOf(testIds);

        // Verify items are marked as processed in database
        await this.VerifyOutboxStatusAsync(testIds, 2); // Status = Done
    }

    [Fact]
    public async Task Worker_WithProcessingFailure_AbandonsItems()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(2);
        this.worker!.ShouldFailProcessing = true;
        this.worker.ProcessingDelay = TimeSpan.FromMilliseconds(50); // Shorter delay for testing
        this.worker.RunOnce = true;

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await this.worker.StartAsync(cts.Token);

        // Give the worker time to claim and attempt processing
        await Task.Delay(3000, cts.Token);
        await this.worker.StopAsync(cts.Token);

        // Assert
        // Items should be abandoned and back to ready state
        await this.VerifyOutboxStatusAsync(testIds, 0); // Status = Ready
    }

    [Fact]
    public async Task Worker_ClaimsItemsCorrectly()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(2);

        // Act - claim items manually to test the claim operation
        var claimedIds = await this.outboxService!.ClaimAsync(Guid.NewGuid(), 30, 10);

        // Assert
        claimedIds.Count.ShouldBe(2);
        claimedIds.ShouldBeSubsetOf(testIds);

        // Verify items are now in InProgress state
        await this.VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    [Fact]
    public async Task Manual_AbandonOperation_Works()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(2);
        var ownerToken = Guid.NewGuid();

        // Act
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);
        await this.VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress

        await this.outboxService.AbandonAsync(ownerToken, claimedIds);

        // Assert
        await this.VerifyOutboxStatusAsync(claimedIds, 0); // Status = Ready
    }

    [Fact]
    public async Task WorkQueue_LeaseExpiration_AllowsReclaim()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();

        // Act - first owner claims with short lease
        var claimed1 = await this.outboxService!.ClaimAsync(owner1, 1, 10); // 1 second lease
        claimed1.Count.ShouldBe(1);

        // Wait for lease to expire
        await Task.Delay(1500);

        // Reap expired items
        await this.outboxService.ReapExpiredAsync();

        // Second owner should be able to claim the same item
        var claimed2 = await this.outboxService.ClaimAsync(owner2, 30, 10);

        // Assert
        claimed2.Count.ShouldBe(1);
        claimed2[0].ShouldBe(claimed1[0]); // Same item
    }

    [Fact]
    public async Task WorkQueue_IdempotentOperations_NoErrors()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var ownerToken = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(ownerToken, 30, 10);

        // Act - multiple acks should be harmless
        await this.outboxService.AckAsync(ownerToken, claimedIds);
        await this.outboxService.AckAsync(ownerToken, claimedIds); // Second ack
        await this.outboxService.AckAsync(ownerToken, claimedIds); // Third ack

        // Assert - should remain acknowledged
        await this.VerifyOutboxStatusAsync(claimedIds, 2); // Status = Done
    }

    [Fact]
    public async Task WorkQueue_UnauthorizedOwner_CannotModify()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(1);
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        var claimedIds = await this.outboxService!.ClaimAsync(owner1, 30, 10);

        // Act - different owner tries to ack
        await this.outboxService.AckAsync(owner2, claimedIds);

        // Assert - item should still be claimed by original owner
        await this.VerifyOutboxStatusAsync(claimedIds, 1); // Status = InProgress
    }

    [Fact]
    public async Task WorkQueue_EmptyIdLists_NoErrors()
    {
        // Arrange
        var ownerToken = Guid.NewGuid();
        var emptyIds = new List<Guid>();

        // Act & Assert - should not throw
        await this.outboxService!.AckAsync(ownerToken, emptyIds);
        await this.outboxService.AbandonAsync(ownerToken, emptyIds);
        await this.outboxService.FailAsync(ownerToken, emptyIds);
    }

    [Fact]
    public async Task WorkQueue_ConcurrentClaims_NoOverlap()
    {
        // Arrange
        var testIds = await this.CreateTestOutboxItemsAsync(10);
        var tasks = new List<Task<IReadOnlyList<Guid>>>();

        // Act - multiple workers claim simultaneously
        for (int i = 0; i < 5; i++)
        {
            var ownerToken = Guid.NewGuid();
            tasks.Add(this.outboxService!.ClaimAsync(ownerToken, 30, 3));
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
        var testIds = await this.CreateTestOutboxItemsAsync(5);
        this.worker!.ProcessingDelay = TimeSpan.FromSeconds(10); // Long delay

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await this.worker.StartAsync(cts.Token);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await this.worker.StopAsync(cts.Token);
        stopwatch.Stop();

        // Assert
        // Worker should stop quickly due to cancellation
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    private async Task<List<Guid>> CreateTestOutboxItemsAsync(int count)
    {
        var ids = new List<Guid>();

        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);

            await connection.ExecuteAsync(@"
                INSERT INTO dbo.Outbox (Id, Topic, Payload, Status, CreatedAt)
                VALUES (@Id, @Topic, @Payload, 0, SYSUTCDATETIME())",
                new { Id = id, Topic = "test", Payload = $"payload{i}" });
        }

        return ids;
    }

    private async Task VerifyOutboxStatusAsync(IEnumerable<Guid> ids, int expectedStatus)
    {
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();

        foreach (var id in ids)
        {
            var status = await connection.ExecuteScalarAsync<int>(
                "SELECT Status FROM dbo.Outbox WHERE Id = @Id", new { Id = id });
            status.ShouldBe(expectedStatus);
        }
    }

    private class TestOutboxWorker : BackgroundService
    {
        private readonly IOutbox outbox;
        private readonly ILogger<TestOutboxWorker> logger;
        private readonly Guid ownerToken = Guid.NewGuid();

        public TestOutboxWorker(IOutbox outbox, ILogger<TestOutboxWorker> logger)
        {
            this.outbox = outbox;
            this.logger = logger;
        }

        public List<Guid> ProcessedItems { get; } = new();
        public bool ShouldFailProcessing { get; set; }
        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public bool RunOnce { get; set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var claimedIds = await this.outbox.ClaimAsync(this.ownerToken, 30, 10, stoppingToken);
                    this.logger.LogInformation("Worker claimed {Count} items", claimedIds.Count);

                    if (claimedIds.Count == 0)
                    {
                        if (this.RunOnce) break;
                        await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                        continue;
                    }

                    var succeededIds = new List<Guid>();
                    var failedIds = new List<Guid>();

                    foreach (var id in claimedIds)
                    {
                        try
                        {
                            await Task.Delay(this.ProcessingDelay, stoppingToken);

                            if (this.ShouldFailProcessing)
                            {
                                this.logger.LogInformation("Simulating failure for item {Id}", id);
                                throw new InvalidOperationException("Simulated processing failure");
                            }

                            this.ProcessedItems.Add(id);
                            succeededIds.Add(id);
                            this.logger.LogInformation("Successfully processed item {Id}", id);
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogWarning(ex, "Failed to process outbox item {Id}", id);
                            failedIds.Add(id);
                        }
                    }

                    if (succeededIds.Count > 0)
                    {
                        this.logger.LogInformation("Acknowledging {Count} successful items", succeededIds.Count);
                        await this.outbox.AckAsync(this.ownerToken, succeededIds, stoppingToken);
                    }

                    if (failedIds.Count > 0)
                    {
                        this.logger.LogInformation("Abandoning {Count} failed items", failedIds.Count);
                        await this.outbox.AbandonAsync(this.ownerToken, failedIds, stoppingToken);
                    }

                    if (this.RunOnce)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    this.logger.LogInformation("Worker cancelled due to stopping token");
                    break;
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Error in outbox processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
    }

}