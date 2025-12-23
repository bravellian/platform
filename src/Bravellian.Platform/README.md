# Bravellian.Platform

Core platform services for SQL-backed work queues, scheduling, and distributed coordination.

## Install

```bash
dotnet add package Bravellian.Platform
```

## Usage

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
