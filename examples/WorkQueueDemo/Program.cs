using Bravellian.Platform;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkQueueDemo;

/// <summary>
/// Demonstrates the work queue pattern implementation.
/// This example shows how to set up and use work queues for processing outbox messages and timers.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Work Queue Demo - Bravellian Platform");
        Console.WriteLine("=====================================");
        
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var connectionString = args[0];
        Console.WriteLine($"Using connection string: {connectionString}");

        var builder = Host.CreateApplicationBuilder();
        
        // Add logging
        builder.Logging.AddConsole();
        
        // Add platform services with work queues
        builder.Services.AddSqlOutbox(connectionString);
        builder.Services.AddSqlScheduler(connectionString);
        
        // Add demo workers
        builder.Services.AddHostedService<DemoOutboxWorker>();
        builder.Services.AddHostedService<DemoTimerWorker>();
        
        var host = builder.Build();

        Console.WriteLine("\nStarting work queue demo...");
        Console.WriteLine("This will:");
        Console.WriteLine("1. Set up database schema");
        Console.WriteLine("2. Add sample outbox messages and timers");
        Console.WriteLine("3. Start worker processes to demonstrate claim/ack pattern");
        Console.WriteLine("4. Run for 30 seconds then exit");
        Console.WriteLine("\nPress Ctrl+C to stop early");

        // Set up the database schema
        await EnsureSchemaAsync(connectionString);
        
        // Add some sample data
        await AddSampleDataAsync(connectionString);

        // Run the demo
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nDemo completed successfully!");
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage: WorkQueueDemo <connection-string>");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  WorkQueueDemo \"Server=(localdb)\\MSSQLLocalDB;Database=WorkQueueDemo;Trusted_Connection=true;TrustServerCertificate=true\"");
        Console.WriteLine();
        Console.WriteLine("This demo will:");
        Console.WriteLine("- Create the necessary database schema");
        Console.WriteLine("- Insert sample outbox messages and timers");
        Console.WriteLine("- Start background workers that demonstrate the work queue pattern");
        Console.WriteLine("- Show claim/ack/abandon operations in real-time");
    }

    private static async Task EnsureSchemaAsync(string connectionString)
    {
        Console.WriteLine("\n📝 Setting up database schema...");
        
        try
        {
            await DatabaseSchemaManager.EnsureOutboxSchemaAsync(connectionString);
            await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(connectionString);
            
            Console.WriteLine("✅ Database schema ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Schema setup failed: {ex.Message}");
            throw;
        }
    }

    private static async Task AddSampleDataAsync(string connectionString)
    {
        Console.WriteLine("\n📝 Adding sample data...");
        
        try
        {
            var outboxWorkQueue = new SqlOutboxWorkQueue(new SqlOutboxOptions 
            { 
                ConnectionString = connectionString, 
                SchemaName = "dbo", 
                TableName = "Outbox" 
            });
            
            var timerWorkQueue = new SqlTimerWorkQueue(connectionString);

            // Add sample outbox messages
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Insert sample outbox messages
                for (int i = 1; i <= 5; i++)
                {
                    await connection.ExecuteAsync(
                        "INSERT INTO dbo.Outbox (Topic, Payload, CreatedAt) VALUES (@Topic, @Payload, SYSDATETIMEOFFSET())",
                        new { Topic = $"demo.message.{i}", Payload = $"Sample message content {i}" });
                }

                // Insert sample timers (some due now, some in future)
                await connection.ExecuteAsync(
                    "INSERT INTO dbo.Timers (Topic, Payload, DueTime, CreatedAt) VALUES (@Topic, @Payload, @DueTime, SYSDATETIMEOFFSET())",
                    new { Topic = "demo.timer.due", Payload = "Timer that's due now", DueTime = DateTimeOffset.UtcNow.AddSeconds(-1) });

                await connection.ExecuteAsync(
                    "INSERT INTO dbo.Timers (Topic, Payload, DueTime, CreatedAt) VALUES (@Topic, @Payload, @DueTime, SYSDATETIMEOFFSET())",
                    new { Topic = "demo.timer.future", Payload = "Timer for future", DueTime = DateTimeOffset.UtcNow.AddHours(1) });
            }

            Console.WriteLine("✅ Sample data added");
            Console.WriteLine("   - 5 outbox messages");
            Console.WriteLine("   - 1 due timer"); 
            Console.WriteLine("   - 1 future timer");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Sample data setup failed: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Demo worker that processes outbox messages using the work queue pattern.
/// </summary>
public class DemoOutboxWorker : BackgroundService
{
    private readonly IOutboxWorkQueue workQueue;
    private readonly ILogger<DemoOutboxWorker> logger;
    private readonly Guid ownerToken = Guid.NewGuid();

    public DemoOutboxWorker(IOutboxWorkQueue workQueue, ILogger<DemoOutboxWorker> logger)
    {
        this.workQueue = workQueue;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation("🚀 OutboxWorker started (Owner: {OwnerToken})", this.ownerToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await this.workQueue.ClaimAsync(this.ownerToken, 30, 10, stoppingToken);
                if (claimed.Count == 0)
                {
                    await Task.Delay(2000, stoppingToken);
                    continue;
                }

                this.logger.LogInformation("📦 Claimed {Count} outbox messages", claimed.Count);

                var succeeded = new List<Guid>();
                var failed = new List<Guid>();

                foreach (var id in claimed)
                {
                    try
                    {
                        // Simulate processing work
                        await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);
                        
                        this.logger.LogInformation("✅ Processed outbox message {Id}", id);
                        succeeded.Add(id);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning("❌ Failed to process outbox message {Id}: {Error}", id, ex.Message);
                        failed.Add(id);
                    }
                }

                if (succeeded.Count > 0)
                {
                    await this.workQueue.AckAsync(this.ownerToken, succeeded, stoppingToken);
                    this.logger.LogInformation("✅ Acknowledged {Count} messages", succeeded.Count);
                }

                if (failed.Count > 0)
                {
                    await this.workQueue.AbandonAsync(this.ownerToken, failed, stoppingToken);
                    this.logger.LogInformation("🔄 Abandoned {Count} messages for retry", failed.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "💥 Error in outbox worker");
                await Task.Delay(5000, stoppingToken);
            }
        }

        this.logger.LogInformation("🛑 OutboxWorker stopped");
    }
}

/// <summary>
/// Demo worker that processes due timers using the work queue pattern.
/// </summary>
public class DemoTimerWorker : BackgroundService
{
    private readonly ITimerWorkQueue workQueue;
    private readonly ILogger<DemoTimerWorker> logger;
    private readonly Guid ownerToken = Guid.NewGuid();

    public DemoTimerWorker(ITimerWorkQueue workQueue, ILogger<DemoTimerWorker> logger)
    {
        this.workQueue = workQueue;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation("⏰ TimerWorker started (Owner: {OwnerToken})", this.ownerToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await this.workQueue.ClaimAsync(this.ownerToken, 30, 5, stoppingToken);
                if (claimed.Count == 0)
                {
                    await Task.Delay(3000, stoppingToken);
                    continue;
                }

                this.logger.LogInformation("⏰ Claimed {Count} due timers", claimed.Count);

                var succeeded = new List<Guid>();

                foreach (var id in claimed)
                {
                    try
                    {
                        // Simulate timer execution
                        await Task.Delay(Random.Shared.Next(200, 800), stoppingToken);
                        
                        this.logger.LogInformation("⏰ Executed timer {Id}", id);
                        succeeded.Add(id);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning("❌ Failed to execute timer {Id}: {Error}", id, ex.Message);
                    }
                }

                if (succeeded.Count > 0)
                {
                    await this.workQueue.AckAsync(this.ownerToken, succeeded, stoppingToken);
                    this.logger.LogInformation("✅ Acknowledged {Count} timers", succeeded.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "💥 Error in timer worker");
                await Task.Delay(5000, stoppingToken);
            }
        }

        this.logger.LogInformation("🛑 TimerWorker stopped");
    }
}