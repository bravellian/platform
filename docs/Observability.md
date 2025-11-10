# Platform Observability

The Platform library includes comprehensive observability features including metrics, a watchdog service, heartbeat monitoring, and health checks.

## Overview

The observability package provides:

- **Metrics** using .NET's `System.Diagnostics.Metrics` API with OpenTelemetry-friendly naming
- **Watchdog Service** that continuously monitors platform components and raises alerts
- **Heartbeat** to verify the watchdog itself is alive
- **Health Checks** integrated with `Microsoft.Extensions.Diagnostics.HealthChecks`
- **Optional Logging** for state transitions (disabled by default)

## Quick Start

### Basic Registration

```csharp
using Bravellian.Platform.Observability;

// Register observability services
services.AddPlatformObservability();
```

### With Configuration

```csharp
services.AddPlatformObservability(options =>
{
    options.EnableMetrics = true;           // default: true
    options.EnableLogging = false;          // default: false
    options.MetricsPrefix = "bravellian.platform";

    options.Watchdog = new WatchdogOptions
    {
        ScanPeriod = TimeSpan.FromSeconds(15),
        HeartbeatPeriod = TimeSpan.FromSeconds(30),
        HeartbeatTimeout = TimeSpan.FromSeconds(90),
        
        // Thresholds for alert detection
        JobOverdueThreshold = TimeSpan.FromSeconds(30),
        InboxStuckThreshold = TimeSpan.FromMinutes(5),
        OutboxStuckThreshold = TimeSpan.FromMinutes(5),
        ProcessorIdleThreshold = TimeSpan.FromMinutes(1),
        
        // Alert cooldown per key
        AlertCooldown = TimeSpan.FromMinutes(2),
    };
});
```

### Adding Alert and Heartbeat Sinks

```csharp
services.AddPlatformObservability()
    .AddWatchdogAlertSink(async (context, ct) =>
    {
        // Route alerts anywhere (queue, email, pager, Teams, etc.)
        Console.WriteLine($"Alert: {context.Kind} - {context.Message}");
        await MyNotificationService.SendAsync(context, ct);
    })
    .AddHeartbeatSink((context, ct) =>
    {
        // Optional: track heartbeat
        Console.WriteLine($"Heartbeat #{context.SequenceNumber} at {context.Timestamp}");
        return Task.CompletedTask;
    })
    .AddPlatformHealthChecks();
```

### Exposing Health Checks

```csharp
// In Program.cs
app.MapHealthChecks("/health");

// Or with custom options
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("platform"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

## Metrics

The following metrics are available via the `Bravellian.Platform` meter:

### Watchdog & Heartbeat
- `bravellian.platform.watchdog.heartbeat_total` (counter) - Total heartbeats emitted
- `bravellian.platform.watchdog.alerts_total` (counter) - Total alerts raised (tags: `kind`, `component`)

### Scheduler
- `bravellian.platform.scheduler.jobs_due_total` (counter) - Jobs that became due
- `bravellian.platform.scheduler.jobs_executed_total` (counter, tags: `job_type`) - Jobs executed
- `bravellian.platform.scheduler.job_delay` (histogram, unit: `s`, tags: `job_type`) - Job delay (start - due time)
- `bravellian.platform.scheduler.job_runtime` (histogram, unit: `s`, tags: `job_type`) - Job execution duration

### Outbox
- `bravellian.platform.outbox.enqueued_total` (counter, tags: `queue`) - Messages enqueued
- `bravellian.platform.outbox.dequeued_total` (counter, tags: `queue`) - Messages dequeued
- `bravellian.platform.outbox.inflight` (updown counter, tags: `queue`) - In-flight messages

### Inbox
- `bravellian.platform.inbox.received_total` (counter, tags: `queue`) - Messages received
- `bravellian.platform.inbox.processed_total` (counter, tags: `queue`, `result`) - Messages processed (result: ok|retry|deadletter)
- `bravellian.platform.inbox.deadlettered_total` (counter, tags: `queue`, `reason`) - Messages dead-lettered

### QoS
- `bravellian.platform.qos.retry_total` (counter, tags: `component`, `reason`) - Retry attempts
- `bravellian.platform.qos.retry_delay` (histogram, unit: `s`, tags: `component`) - Retry delay duration

## OpenTelemetry Integration

To export metrics to OpenTelemetry:

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Bravellian.Platform");
        // Add other meters as needed
        metrics.AddPrometheusExporter(); // or OTLP, etc.
    });
```

## Alert Types

The watchdog can detect and raise the following alert types:

- **OverdueJob** - A scheduled job is overdue beyond the threshold
- **StuckInbox** - An inbox message is stuck beyond the threshold
- **StuckOutbox** - An outbox message is stuck beyond the threshold
- **ProcessorNotRunning** - A processor loop is idle or not running
- **HeartbeatStale** - The watchdog heartbeat is stale (used by health checks)

## State Providers

To enable watchdog monitoring for specific components, implement the state provider interfaces:

```csharp
public interface ISchedulerState
{
    Task<IReadOnlyList<(string JobId, DateTimeOffset DueTime)>> GetOverdueJobsAsync(
        TimeSpan threshold, CancellationToken cancellationToken);
}

public interface IInboxState
{
    Task<IReadOnlyList<(string MessageId, string Queue, DateTimeOffset ReceivedAt)>> GetStuckMessagesAsync(
        TimeSpan threshold, CancellationToken cancellationToken);
}

public interface IOutboxState
{
    Task<IReadOnlyList<(string MessageId, string Queue, DateTimeOffset CreatedAt)>> GetStuckMessagesAsync(
        TimeSpan threshold, CancellationToken cancellationToken);
}

public interface IProcessingState
{
    Task<IReadOnlyList<(string ProcessorId, string Component, DateTimeOffset LastActivityAt)>> GetIdleProcessorsAsync(
        TimeSpan threshold, CancellationToken cancellationToken);
}
```

Register your implementations:

```csharp
services.AddSingleton<ISchedulerState, MySchedulerState>();
services.AddSingleton<IInboxState, MyInboxState>();
// etc.
```

## Interrogating Watchdog State

You can query the watchdog state at any time:

```csharp
public class DiagnosticsController : ControllerBase
{
    private readonly IWatchdog watchdog;

    public DiagnosticsController(IWatchdog watchdog)
    {
        this.watchdog = watchdog;
    }

    [HttpGet("/diagnostics/watchdog")]
    public IActionResult GetWatchdogSnapshot()
    {
        var snapshot = this.watchdog.GetSnapshot();
        return Ok(new
        {
            snapshot.LastScanAt,
            snapshot.LastHeartbeatAt,
            ActiveAlertCount = snapshot.ActiveAlerts.Count,
            Alerts = snapshot.ActiveAlerts.Select(a => new
            {
                a.Kind,
                a.Component,
                a.Key,
                a.Message,
                a.FirstSeenAt,
                a.LastSeenAt
            })
        });
    }
}
```

## Health Check Behavior

The `WatchdogHealthCheck` returns:

- **Healthy** - No alerts, heartbeat is current
- **Degraded** - Warning-level alerts present (StuckInbox, StuckOutbox)
- **Unhealthy** - Critical alerts present (OverdueJob, ProcessorNotRunning) or heartbeat is stale

## Configuration Defaults

| Setting | Default | Description |
|---------|---------|-------------|
| `ScanPeriod` | 15s | Watchdog scan interval (±10% jitter applied) |
| `HeartbeatPeriod` | 30s | Heartbeat emission interval |
| `HeartbeatTimeout` | 90s | Threshold for stale heartbeat |
| `JobOverdueThreshold` | 30s | Time past due before job alert |
| `InboxStuckThreshold` | 5m | Age threshold for stuck inbox messages |
| `OutboxStuckThreshold` | 5m | Age threshold for stuck outbox messages |
| `ProcessorIdleThreshold` | 1m | Idle time threshold for processors |
| `AlertCooldown` | 2m | Re-emission window per alert key |
| `EnableMetrics` | true | Enable metrics collection |
| `EnableLogging` | false | Enable optional logging |

## Example: Complete Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Bravellian.Platform");
        metrics.AddPrometheusExporter();
    });

// Add Platform Observability
builder.Services
    .AddPlatformObservability(options =>
    {
        options.Watchdog.ScanPeriod = TimeSpan.FromSeconds(10);
    })
    .AddWatchdogAlertSink(async (alert, ct) =>
    {
        // Send to your notification system
        await alertService.SendAsync(alert, ct);
    })
    .AddHeartbeatSink((heartbeat, ct) =>
    {
        // Optional: Track heartbeats
        return Task.CompletedTask;
    })
    .AddPlatformHealthChecks();

var app = builder.Build();

// Expose metrics
app.MapPrometheusScrapingEndpoint();

// Expose health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
```

## Troubleshooting

### Watchdog Not Detecting Issues

Ensure you've registered state providers for the components you want to monitor:
```csharp
services.AddSingleton<ISchedulerState, YourSchedulerState>();
```

### No Metrics Appearing

Verify the meter is added to your metrics configuration:
```csharp
metrics.AddMeter("Bravellian.Platform");
```

### Health Check Always Unhealthy

Check the heartbeat timeout configuration and ensure the watchdog service is running.

## Best Practices

1. **Alert Routing** - Use alert attributes to route different alert types to appropriate channels
2. **Cooldown Tuning** - Adjust `AlertCooldown` based on your notification preferences
3. **Threshold Tuning** - Set thresholds appropriate for your workload characteristics
4. **Metrics Sampling** - Use histogram boundaries appropriate for your latency targets
5. **State Providers** - Keep state queries efficient; they run frequently
6. **Logging** - Leave `EnableLogging = false` in production unless debugging

## Metrics Exporter

The platform includes an in-app metrics exporter that stores metrics data in SQL Server for long-term storage, querying, and alerting without requiring external telemetry infrastructure.

### Architecture

- **Per-Instance Export**: Each process instance runs its own exporter that subscribes to `Bravellian.Platform` meters via `MeterListener`
- **Minute Granularity**: Metrics are aggregated into 1-minute buckets and written to application databases
- **Hourly Rollups**: Data is also aggregated hourly and written to a central database for cross-database analysis
- **Additive Upserts**: Multiple instances can safely write to the same buckets concurrently using additive SQL operations
- **Automatic Retention**: Scheduled jobs clean up old data (14 days for minute data, 90 days for hourly data)

### Database Schema

The exporter creates tables in the `infra` schema:

**Application Databases:**
- `infra.MetricDef` - Metric definitions (name, unit, aggregation kind)
- `infra.MetricSeries` - Time series keys (metric + service + instance + tags)
- `infra.MetricPointMinute` - Minute-level data points

**Central Database:**
- `infra.MetricSeries` - Time series keys with DatabaseId for cross-database aggregation
- `infra.MetricPointHourly` - Hourly rollups
- `infra.ExporterHeartbeat` - Exporter instance health tracking

### Quick Start

```csharp
// Register the metrics exporter
services.AddMetricsExporter(options =>
{
    options.Enabled = true;
    options.ServiceName = "MyService";
    options.FlushInterval = TimeSpan.FromSeconds(60);
    options.EnableCentralRollup = true;
    options.CentralConnectionString = "Server=central;...";
    options.MinuteRetentionDays = 14;
    options.HourlyRetentionDays = 90;
});

// Add health check
services.AddMetricsExporterHealthCheck();
```

### Stored Metrics

The exporter captures all metrics from the `Bravellian.Platform` meter, including:

- Watchdog heartbeats and alerts
- Scheduler job execution and delays
- Outbox enqueue/dequeue operations
- Inbox message processing
- QoS retry operations

### Scheduled Jobs

Three background jobs support the exporter:

1. **MetricsRetentionMinuteJob** - Deletes minute data older than retention period from application databases
2. **MetricsRetentionHourlyJob** - Deletes hourly data older than retention period from central database
3. **MetricsExporterFreshnessJob** - Monitors exporter heartbeats and logs warnings for stale instances

These jobs can be registered with the scheduler:

```csharp
// In your job registration code
await schedulerClient.CreateOrUpdateJobAsync(
    "metrics.retention.minute",
    "platform.metrics.retention.minute",
    "0 0 2 * * *", // Daily at 2 AM
    payload: null);
```

### Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | true | Enable/disable the metrics exporter |
| `ServiceName` | "Unknown" | Service name tag for this instance |
| `FlushInterval` | 60s | How often to flush metrics to the database |
| `ReservoirSize` | 1000 | Sample size for percentile calculations |
| `EnableCentralRollup` | true | Enable hourly rollups to central database |
| `CentralConnectionString` | null | Connection string for central database |
| `MinuteRetentionDays` | 14 | Days to keep minute-level data |
| `HourlyRetentionDays` | 90 | Days to keep hourly rollup data |

### Health Monitoring

The `MetricsExporterHealthCheck` reports:

- **Healthy** - Last flush within 2 minutes, no errors
- **Degraded** - Last flush successful but errors present
- **Unhealthy** - Last flush older than 2 minutes

Access via standard health check endpoints:

```csharp
app.MapHealthChecks("/health");
```

### Querying Metrics

Query metrics directly from SQL:

```sql
-- Get outbox published count for the last hour
SELECT 
    SUM(m.ValueSum) as TotalPublished
FROM infra.MetricSeries s
JOIN infra.MetricPointMinute m ON s.SeriesId = m.SeriesId
JOIN infra.MetricDef d ON s.MetricDefId = d.MetricDefId
WHERE d.Name = 'bravellian.platform.outbox.enqueued_total'
  AND m.BucketStartUtc >= DATEADD(HOUR, -1, GETUTCDATE());

-- Get P95 latency across all instances
SELECT 
    MAX(m.P95) as MaxP95Latency
FROM infra.MetricSeries s
JOIN infra.MetricPointMinute m ON s.SeriesId = m.SeriesId
JOIN infra.MetricDef d ON s.MetricDefId = d.MetricDefId
WHERE d.Name = 'bravellian.platform.scheduler.job_runtime'
  AND m.BucketStartUtc >= DATEADD(HOUR, -1, GETUTCDATE());
```

## Future Enhancements

Planned for v1.1+:

- Per-queue/per-job custom thresholds
- Tenant/shard dimension tags
- Alert severity levels
- Hysteresis for alert transitions
- Dashboard templates
- More granular processor monitoring
- Fixed-bucket histograms for precise global percentiles
- Metrics export to external systems (Prometheus, OpenTelemetry)
