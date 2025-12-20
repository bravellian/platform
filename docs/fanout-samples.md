# Fanout quick samples

These snippets show minimal, runnable configurations for fanout. They include dependency injection setup, handler/planner wiring, and health checks so you can drop them into a new console host and run `dotnet run` with a valid SQL Server connection string.

## Single-tenant fanout

```csharp
using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

const string connectionString = "Server=localhost;Database=Platform;Trusted_Connection=True;TrustServerCertificate=True";

// Register platform with single database
builder.Services.AddPlatformMultiDatabaseWithList(
    new[]
    {
        new PlatformDatabase
        {
            Name = "Platform",
            ConnectionString = connectionString,
        }
    },
    enableSchemaDeployment: true);

builder.Services.AddFanoutTopic<ReportFanoutPlanner>(new FanoutTopicOptions
{
    FanoutTopic = "reports",
    Cron = "*/5 * * * *", // every 5 minutes
});

builder.Services.AddOutboxHandler<ReportSliceHandler>();

builder.Services
    .AddHealthChecks()
    .AddSqlSchedulerHealthCheck();

builder.Services.AddLogging();

builder.Services.AddScoped<ReportFanoutPlanner>();
builder.Services.AddScoped<ReportSliceHandler>();

await builder.Build().RunAsync();

// Example planner
public sealed class ReportFanoutPlanner : BaseFanoutPlanner
{
    public ReportFanoutPlanner(IFanoutPolicyRepository policyRepository, IFanoutCursorRepository cursorRepository)
        : base(policyRepository, cursorRepository)
    {
    }

    protected override async IAsyncEnumerable<(string shardKey, string workKey)> EnumerateCandidatesAsync(
        string fanoutTopic,
        string? workKey,
        CancellationToken ct)
    {
        // Fanout over two shards; replace with your own discovery logic
        yield return ("shard-a", workKey ?? "default");
        yield return ("shard-b", workKey ?? "default");
        await Task.CompletedTask;
    }
}

// Example handler for slices emitted by the dispatcher
public sealed class ReportSliceHandler : IOutboxHandler
{
    public string Topic => "fanout:reports:default";

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Process slice payload: {message.Payload}");
        return Task.CompletedTask;
    }
}
```

## Multi-tenant fanout across two databases

```csharp
using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register platform with multiple databases
builder.Services.AddPlatformMultiDatabaseWithList(
    new[]
    {
        new PlatformDatabase
        {
            Name = "TenantA",
            ConnectionString = "Server=localhost;Database=TenantA;Trusted_Connection=True;TrustServerCertificate=True",
        },
        new PlatformDatabase
        {
            Name = "TenantB",
            ConnectionString = "Server=localhost;Database=TenantB;Trusted_Connection=True;TrustServerCertificate=True",
        }
    },
    enableSchemaDeployment: true);

builder.Services.AddFanoutTopic<InventoryFanoutPlanner>(new FanoutTopicOptions
{
    FanoutTopic = "inventory",
    DefaultEverySeconds = 120,
});

builder.Services.AddOutboxHandler<InventorySliceHandler>();
builder.Services.AddHealthChecks().AddSqlSchedulerHealthCheck();
builder.Services.AddLogging();

builder.Services.AddScoped<InventoryFanoutPlanner>();
builder.Services.AddScoped<InventorySliceHandler>();

await builder.Build().RunAsync();

public sealed class InventoryFanoutPlanner : BaseFanoutPlanner
{
    public InventoryFanoutPlanner(IFanoutPolicyRepository policyRepository, IFanoutCursorRepository cursorRepository)
        : base(policyRepository, cursorRepository)
    {
    }

    protected override async IAsyncEnumerable<(string shardKey, string workKey)> EnumerateCandidatesAsync(
        string fanoutTopic,
        string? workKey,
        CancellationToken ct)
    {
        yield return ("stock", workKey ?? "default");
        await Task.CompletedTask;
    }
}

public sealed class InventorySliceHandler : IOutboxHandler
{
    public string Topic => "fanout:inventory:default";

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Tenant slice: {message.Payload}");
        return Task.CompletedTask;
    }
}
```
