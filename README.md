# Bravellian Platform

.NET 10 platform for SQL-backed distributed work-queue primitives (outbox, inbox, schedulers, fanout, leases) with claim-ack-abandon semantics and database-authoritative timing.

## What you get

Bravellian Platform provides durable background processing and coordination primitives that are safe to use in multi-node services:

- Outbox and inbox for reliable publishing and idempotent consumption.
- One-time and recurring scheduling with database-authoritative timing.
- Fanout/join coordination built on the same work-queue model.
- Leases and semaphores for distributed locking.
- Consistent observability, audit, and operations tracking.

## Providers

Choose a storage provider (or use InMemory for tests/dev):

- `Bravellian.Platform.SqlServer`
- `Bravellian.Platform.Postgres`
- `Bravellian.Platform.InMemory`

Providers can auto-deploy schema (recommended for local/dev) or you can run scripts manually.

## Quick start (SQL Server)

Install:

```bash
dotnet add package Bravellian.Platform
dotnet add package Bravellian.Platform.SqlServer
```

Register the full platform stack:

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

Postgres uses `AddPostgresPlatform` with the same options and tuning hooks.

## Discovery-based multi-database registration

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services.AddSqlPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: true);
```

## Package map

Core and providers:

- `Bravellian.Platform` (core abstractions and orchestration)
- `Bravellian.Platform.SqlServer`, `Bravellian.Platform.Postgres` (storage providers)
- `Bravellian.Platform.InMemory` (test/dev provider)

Platform capabilities:

- `Bravellian.Platform.Audit` (immutable audit timeline)
- `Bravellian.Platform.Operations` (long-running operations tracking)
- `Bravellian.Platform.Observability` (shared conventions and emitters)
- `Bravellian.Platform.Idempotency` (TryBegin/Complete/Fail guard)
- `Bravellian.Platform.ExactlyOnce` (best-effort exactly-once workflow)
- `Bravellian.Platform.Email` + `Bravellian.Platform.Email.Postmark` + `Bravellian.Platform.Email.AspNetCore`
- `Bravellian.Platform.Webhooks` + `Bravellian.Platform.Webhooks.AspNetCore`
- `Bravellian.Platform.Modularity` + `Bravellian.Platform.Modularity.AspNetCore` + `Bravellian.Platform.Modularity.Razor`
- `Bravellian.Platform.Metrics.AspNetCore`, `Bravellian.Platform.Metrics.HttpServer`
- `Bravellian.Platform.Correlation`
- `Bravellian.Platform.HealthProbe`

## Database schema

SQL Server artifacts live in `src/Bravellian.Platform.SqlServer/Database/`. Use provider options to auto-deploy, or run scripts manually in controlled environments.

## Documentation

Start here:

- `docs/INDEX.md` (documentation index)
- `docs/GETTING_STARTED.md` (getting started guide)
- `docs/outbox-quickstart.md` and `docs/inbox-quickstart.md`
- `docs/observability/README.md`
- `docs/testing/README.md`

Package-specific READMEs live under `src/Bravellian.Platform.*`.

## Tests and smoke app

- Tests live in `tests/Bravellian.Platform.Tests/` and related projects.
- `tests/Bravellian.Platform.SmokeWeb/` is a minimal ASP.NET Core UI for exercising outbox/inbox/scheduler/fanout/leases.
- `tests/Bravellian.Platform.SmokeWeb.AppHost/` is an Aspire app host that can spin up SQL Server and Postgres containers.

## Contributing

See `CONTRIBUTING.md` for development workflow and guidelines.
