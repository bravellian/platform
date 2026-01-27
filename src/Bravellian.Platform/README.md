# Bravellian.Platform

Core platform abstractions and orchestration. SQL Server integrations live in `Bravellian.Platform.SqlServer`.

## Install

```bash
dotnet add package Bravellian.Platform
dotnet add package Bravellian.Platform.SqlServer
```

## Usage

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

## Examples

### One-time execution registry

Use <xref:Bravellian.Platform.OnceExecutionRegistry> to guard idempotent startup tasks or DI registrations.

```csharp
var registry = new OnceExecutionRegistry();

if (!registry.CheckAndMark("platform:di"))
{
    builder.Services.AddSqlPlatform("Server=localhost;Database=MyApp;Trusted_Connection=true;");
}

if (registry.HasRun("platform:di"))
{
    logger.LogInformation("Platform services already registered.");
}
```

### Outbox + Inbox configuration

Use `SqlPlatformOptions.ConfigureOutbox` and `SqlPlatformOptions.ConfigureInbox` to tune
outbox/inbox behavior while keeping a single registration call.

### Discovery-based registration

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services.AddSqlPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: true);
```

## Documentation

- https://github.com/bravellian/platform
- docs/INDEX.md
- docs/outbox-quickstart.md
- docs/inbox-quickstart.md
