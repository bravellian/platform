# Getting Started with Bravellian Platform

Bravellian Platform is a .NET 10 library that provides SQL-backed work-queue primitives (outbox, inbox, scheduler, fanout, leases) with claim-ack-abandon semantics and database-authoritative timing.

## What is Bravellian Platform?

Use the platform when you need durable background processing, safe retries, and coordination across multiple service instances:

- **Outbox** for reliable publishing alongside transactions.
- **Inbox** for idempotent message processing.
- **Schedulers** for one-time and recurring work.
- **Fanout and join** for coordination workflows.
- **Leases and semaphores** for distributed locking.
- **Observability, audit, and operations** conventions across components.

## Requirements

- .NET 10
- SQL Server or Postgres (or InMemory for tests/dev)

## Installation

Add the core package and a provider:

```bash
dotnet add package Bravellian.Platform
dotnet add package Bravellian.Platform.SqlServer
# or
# dotnet add package Bravellian.Platform.Postgres
```

## Quick start: SQL Server

```csharp
using Bravellian.Platform;

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

## Quick start: Postgres

```csharp
using Bravellian.Platform;

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

## Discovery-based multi-database registration

If you already use `IPlatformDatabaseDiscovery` to enumerate tenant databases, reuse the same discovery pipeline for platform features:

```csharp
builder.Services.AddSingleton<IPlatformDatabaseDiscovery>(new MyTenantDiscovery());

builder.Services.AddSqlPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: true);
```

## Using the outbox

```csharp
public class OrderService
{
    private readonly IOutbox _outbox;

    public async Task CreateOrderAsync(Order order, IDbTransaction transaction)
    {
        await SaveOrderToDatabase(order, transaction);

        await _outbox.EnqueueAsync(
            topic: "order.created",
            payload: JsonSerializer.Serialize(order),
            transaction: transaction,
            correlationId: order.Id.ToString());
    }
}
```

## Using the inbox

```csharp
public class WebhookController : ControllerBase
{
    private readonly IInbox _inbox;

    [HttpPost("webhooks/payment")]
    public async Task<IActionResult> PaymentWebhook([FromBody] PaymentEvent evt)
    {
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(evt.Id, "StripeWebhook");
        if (alreadyProcessed)
        {
            return Ok();
        }

        await _inbox.MarkProcessingAsync(evt.Id);
        await ProcessPaymentAsync(evt);
        await _inbox.MarkProcessedAsync(evt.Id);

        return Ok();
    }
}
```

## Database schema

Provider options can auto-deploy schema, or you can run scripts manually in controlled environments.

SQL Server scripts live under `src/Bravellian.Platform.SqlServer/Database/`.

## Documentation index

- `docs/INDEX.md`
- `docs/outbox-quickstart.md`
- `docs/inbox-quickstart.md`
- `docs/observability/README.md`
- `docs/testing/README.md`

## Example applications

- `tests/Bravellian.Platform.SmokeWeb/` is a minimal ASP.NET Core UI for exercising outbox, inbox, scheduler, fanout, and leases.
- `tests/Bravellian.Platform.Smoke.AppHost/` is an Aspire app host that can spin up SQL Server and Postgres containers.

## Getting help

- `docs/INDEX.md` for the full catalog
- `CONTRIBUTING.md` for development workflow
