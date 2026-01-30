## Health checks, startup latch, and startup checks

This repo treats "bootstrapping" as any startup-time work that must complete before the service is considered live (for example: platform schema deployment, app migrations, or one-time startup validation). The startup latch is the single source of truth for liveness during bootstrapping, while startup checks are the one-time validations that run during startup.

### Adding a bootstrapping step

Use `IStartupLatch` to mark a step as pending until it completes. The latch is designed for the pattern below, which also allows failures to bubble and crash startup (no swallowing).

```csharp
public sealed class StartupBootstrapper : IHostedService
{
    private readonly IStartupLatch latch;
    private readonly IMyMigrator migrator;

    public StartupBootstrapper(IStartupLatch latch, IMyMigrator migrator)
    {
        this.latch = latch;
        this.migrator = migrator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var step = latch.Register("app-migrations");
        await migrator.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Step names should be stable and readable (examples: `app-migrations`, `platform-migrations`, `startup-init`).

### Adding a startup check

Startup checks are one-time gates that execute during startup and optionally block startup if they fail. Keep them bounded (no long retries, short timeouts if networked).

Minimal implementation:

```csharp
public sealed class ConfigRequiredStartupCheck : IStartupCheck
{
    public string Name => "config-required";

    public int Order => -100;

    public bool IsCritical => true;

    public Task ExecuteAsync(CancellationToken ct)
    {
        // Validate configuration here and throw on failure.
        return Task.CompletedTask;
    }
}
```

DI registration:

```csharp
services.AddStartupCheck<ConfigRequiredStartupCheck>();
services.AddStartupCheckRunner();
```

#### Critical vs noncritical checks

- **Critical** checks (`IsCritical == true`) should fail startup when they throw.
- **Noncritical** checks should log and allow startup to continue if they throw.

#### What should/shouldn’t be in startup checks

- **Should:** fast config validation, quick connectivity probes with tight timeouts.
- **Should not:** unbounded retries, long-running background work, or slow external dependencies.

### Endpoint contracts

These are the intended behaviors for host apps that expose health endpoints:

- `/health/live`
  - Liveness only.
  - Uses the startup latch as the only gate.
  - No external dependencies (no DB/Auth/Blob checks).
  - Safe for Docker health checks (fast).

- `/health/ready`
  - Readiness for routing.
  - Includes critical-fast dependencies only (DB/Auth/Blob and similar), plus the startup latch.
  - Also includes the startup latch.
  - Intended for load balancers (for example, Cloudflare) to decide routing/monitoring.

- `/health/status`
  - Full status matrix (includes slower or outbox-backed dependencies when present).
  - Intended for dashboards and diagnostics, not routing decisions.

### What can affect routing

- **Allowed to affect routing:** startup latch, critical-fast dependency checks (DB/Auth/Blob).
- **Dashboards only:** slower checks, outbox-backed dependencies, best-effort or eventually consistent signals.

### Healthcheck targeting

- Docker HEALTHCHECK targets `/health/live` (fast liveness + startup latch).
- `/health/ready` is for routing/monitoring (critical-fast ongoing dependencies).
