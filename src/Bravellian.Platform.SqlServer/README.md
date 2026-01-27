# Bravellian.Platform.SqlServer

SQL Server provider for Bravellian.Platform: outbox, inbox, scheduler, fanout, metrics, leases, and semaphores.

## Install

```bash
dotnet add package Bravellian.Platform.SqlServer
```

## Usage

### Single-call platform registration

Register the full SQL Server-backed platform stack (outbox/inbox/scheduler/fanout/idempotency, audit, operations,
email outbox, metrics exporter, leases, semaphores, external side effects) with one call:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlPlatform(
    "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    options =>
    {
        options.EnableSchemaDeployment = true;
        options.EnableSchedulerWorkers = true;
    });

var app = builder.Build();
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true,
    MaxPollingInterval = TimeSpan.FromSeconds(5)
});

builder.Services.AddHealthChecks()
    .AddSqlSchedulerHealthCheck();

var app = builder.Build();
app.MapHealthChecks("/health");
```

## Examples

### One-time execution registry

Use <xref:Bravellian.Platform.OnceExecutionRegistry> to guard idempotent startup tasks or DI registrations.

```csharp
var registry = new OnceExecutionRegistry();

if (!registry.CheckAndMark("platform:di"))
{
    builder.Services.AddPlatformScheduler();
}

if (registry.HasRun("platform:di"))
{
    logger.LogInformation("Platform services already registered.");
}
```

### Outbox + Inbox

```csharp
builder.Services.AddSqlOutbox(new SqlOutboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true
});

builder.Services.AddSqlInbox(new SqlInboxOptions
{
    ConnectionString = "Server=localhost;Database=MyApp;Trusted_Connection=true;",
    EnableSchemaDeployment = true
});
```

### Discovery-based registration

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services
    .AddPlatformOutbox(enableSchemaDeployment: true)
    .AddPlatformInbox(enableSchemaDeployment: true)
    .AddPlatformScheduler()
    .AddPlatformFanout()
    .AddPlatformLeases();
```

## Documentation

- https://github.com/bravellian/platform
- docs/INDEX.md
- docs/outbox-quickstart.md
- docs/inbox-quickstart.md
