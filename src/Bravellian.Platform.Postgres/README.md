# Bravellian.Platform.Postgres

PostgreSQL provider for Bravellian.Platform: outbox, inbox, scheduler, fanout, metrics, leases, and semaphores.

## Install

```bash
dotnet add package Bravellian.Platform.Postgres
```

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPostgresScheduler(new PostgresSchedulerOptions
{
    ConnectionString = "Host=localhost;Database=MyApp;Username=app;Password=secret;",
    EnableSchemaDeployment = true,
    MaxPollingInterval = TimeSpan.FromSeconds(5)
});

builder.Services.AddHealthChecks()
    .AddPostgresSchedulerHealthCheck();

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
builder.Services.AddPostgresOutbox(new PostgresOutboxOptions
{
    ConnectionString = "Host=localhost;Database=MyApp;Username=app;Password=secret;",
    EnableSchemaDeployment = true
});

builder.Services.AddPostgresInbox(new PostgresInboxOptions
{
    ConnectionString = "Host=localhost;Database=MyApp;Username=app;Password=secret;",
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
