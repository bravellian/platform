namespace Bravellian.Platform.Tests;

using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using Shouldly;

public class WorkQueueWorkerTests : SqlServerTestBase
{
    private OutboxWorkQueueClient? outboxClient;
    private TestOutboxWorker? worker;

    public WorkQueueWorkerTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        
        // Ensure work queue schema is set up
        await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(this.ConnectionString);
        
        this.outboxClient = new OutboxWorkQueueClient(this.ConnectionString);
        this.worker = new TestOutboxWorker(this.outboxClient, NullLogger<TestOutboxWorker>.Instance);
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
        
        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await this.worker.StartAsync(cts.Token);
        
        // Give the worker time to process items
        await Task.Delay(1000, cts.Token);
        await this.worker.StopAsync(cts.Token);

        // Assert
        // Items should be abandoned and back to ready state
        await this.VerifyOutboxStatusAsync(testIds, 0); // Status = Ready
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

    private class TestOutboxWorker : WorkQueueWorkerBase<Guid>
    {
        public TestOutboxWorker(IWorkQueueClient<Guid> workQueueClient, ILogger<TestOutboxWorker> logger)
            : base(workQueueClient, logger)
        {
        }

        public List<Guid> ProcessedItems { get; } = new();
        public bool ShouldFailProcessing { get; set; }
        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        protected override int BatchSize => 10;
        protected override TimeSpan PollingDelay => TimeSpan.FromMilliseconds(100);

        protected override async Task ProcessWorkItemAsync(Guid id, CancellationToken cancellationToken)
        {
            await Task.Delay(this.ProcessingDelay, cancellationToken);
            
            if (this.ShouldFailProcessing)
            {
                throw new InvalidOperationException("Simulated processing failure");
            }

            this.ProcessedItems.Add(id);
        }
    }
}