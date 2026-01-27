# Bravellian.Platform.Postgres

PostgreSQL provider for Bravellian.Platform: outbox, inbox, scheduler, fanout, metrics, leases, and semaphores.

## Install

```bash
dotnet add package Bravellian.Platform.Postgres
```

## Usage

### Single-call platform registration

Register the full PostgreSQL-backed platform stack (outbox/inbox/scheduler/fanout/idempotency, audit, operations,
email outbox + delivery, metrics exporter, leases, semaphores) with one call:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPostgresPlatform(
    "Host=localhost;Database=MyApp;Username=app;Password=secret;",
    options =>
    {
        options.EnableSchemaDeployment = true;
        options.EnableSchedulerWorkers = true;
    });

var app = builder.Build();
```
Use the `ConfigureOutbox`, `ConfigureInbox`, and `ConfigureScheduler` delegates on
`PostgresPlatformOptions` for per-component tuning while keeping a single public entry point.

## Examples

### One-time execution registry

Use <xref:Bravellian.Platform.OnceExecutionRegistry> to guard idempotent startup tasks or DI registrations.

```csharp
var registry = new OnceExecutionRegistry();

if (!registry.CheckAndMark("platform:di"))
{
    builder.Services.AddPostgresPlatform("Host=localhost;Database=MyApp;Username=app;Password=secret;");
}

if (registry.HasRun("platform:di"))
{
    logger.LogInformation("Platform services already registered.");
}
```

### Discovery-based registration

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services.AddPostgresPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: true);
```

## Documentation

- https://github.com/bravellian/platform
- docs/INDEX.md
- docs/outbox-quickstart.md
- docs/inbox-quickstart.md
