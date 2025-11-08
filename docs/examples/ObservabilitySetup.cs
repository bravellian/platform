// Observability Setup Example
// This file shows how to configure platform observability with alerts and monitoring

using Bravellian.Platform.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// 1. Add OpenTelemetry with metrics
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        // Add the Bravellian.Platform meter
        metrics.AddMeter("Bravellian.Platform");
        
        // Add runtime instrumentation (optional)
        metrics.AddRuntimeInstrumentation();
        
        // Add ASP.NET Core instrumentation (optional)
        metrics.AddAspNetCoreInstrumentation();
        
        // Export to Prometheus (or use other exporters like OTLP)
        metrics.AddPrometheusExporter();
    });

// 2. Add Platform Observability with custom configuration
builder.Services
    .AddPlatformObservability(options =>
    {
        // Enable metrics (default: true)
        options.EnableMetrics = true;
        
        // Disable logging (default: false)
        // Set to true only for debugging
        options.EnableLogging = false;
        
        // Configure watchdog behavior
        options.Watchdog = new WatchdogOptions
        {
            // How often to scan for issues (with ±10% jitter)
            ScanPeriod = TimeSpan.FromSeconds(15),
            
            // How often to emit heartbeat
            HeartbeatPeriod = TimeSpan.FromSeconds(30),
            
            // When to consider heartbeat stale
            HeartbeatTimeout = TimeSpan.FromSeconds(90),
            
            // Alert thresholds - tune these for your workload
            JobOverdueThreshold = TimeSpan.FromSeconds(30),
            InboxStuckThreshold = TimeSpan.FromMinutes(5),
            OutboxStuckThreshold = TimeSpan.FromMinutes(5),
            ProcessorIdleThreshold = TimeSpan.FromMinutes(1),
            
            // Re-emit cooldown per alert key
            AlertCooldown = TimeSpan.FromMinutes(2),
        };
    })
    // 3. Add alert sink for notifications
    .AddWatchdogAlertSink(async (alert, ct) =>
    {
        // Route alerts based on kind and component
        switch (alert.Kind)
        {
            case WatchdogAlertKind.OverdueJob:
                // Critical - page on-call
                await SendPagerAlert(alert, ct);
                break;
                
            case WatchdogAlertKind.StuckInbox:
            case WatchdogAlertKind.StuckOutbox:
                // Warning - send to monitoring channel
                await SendSlackAlert(alert, ct);
                break;
                
            case WatchdogAlertKind.ProcessorNotRunning:
                // Critical - immediate attention needed
                await SendPagerAlert(alert, ct);
                break;
        }
        
        // Always log all alerts
        Console.WriteLine($"[ALERT] {alert.Kind} in {alert.Component}: {alert.Message}");
    })
    // 4. Add heartbeat sink (optional)
    .AddHeartbeatSink((heartbeat, ct) =>
    {
        // Optional: Track heartbeat externally
        // e.g., write to a monitoring dashboard or external watchdog service
        Console.WriteLine($"Watchdog heartbeat #{heartbeat.SequenceNumber} at {heartbeat.Timestamp}");
        return Task.CompletedTask;
    })
    // 5. Add health checks
    .AddPlatformHealthChecks();

// 6. Add state providers (implement these based on your platform setup)
// These tell the watchdog how to query your system state
builder.Services.AddSingleton<ISchedulerState, MySchedulerState>();
builder.Services.AddSingleton<IInboxState, MyInboxState>();
builder.Services.AddSingleton<IOutboxState, MyOutboxState>();
builder.Services.AddSingleton<IProcessingState, MyProcessingState>();

var app = builder.Build();

// 7. Expose Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// 8. Expose health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// 9. Optional: Add diagnostics endpoint for watchdog inspection
app.MapGet("/diagnostics/watchdog", (IWatchdog watchdog) =>
{
    var snapshot = watchdog.GetSnapshot();
    return Results.Ok(new
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
            a.LastSeenAt,
            a.Attributes
        })
    });
});

app.Run();

// Helper methods for alert routing
static async Task SendPagerAlert(WatchdogAlertContext alert, CancellationToken ct)
{
    // TODO: Integrate with your paging system (PagerDuty, Opsgenie, etc.)
    Console.WriteLine($"[PAGER] {alert.Message}");
    await Task.CompletedTask;
}

static async Task SendSlackAlert(WatchdogAlertContext alert, CancellationToken ct)
{
    // TODO: Integrate with Slack, Teams, or other messaging platform
    Console.WriteLine($"[SLACK] {alert.Message}");
    await Task.CompletedTask;
}

// Example state provider implementations
// You'll need to implement these based on your actual data access layer

class MySchedulerState : ISchedulerState
{
    public async Task<IReadOnlyList<(string JobId, DateTimeOffset DueTime)>> GetOverdueJobsAsync(
        TimeSpan threshold, CancellationToken cancellationToken)
    {
        // TODO: Query your scheduler store for overdue jobs
        // Example:
        // var cutoff = DateTimeOffset.UtcNow - threshold;
        // return await _db.Jobs
        //     .Where(j => j.Status == "Pending" && j.DueTime < cutoff)
        //     .Select(j => (j.JobId, j.DueTime))
        //     .ToListAsync(cancellationToken);
        
        return Array.Empty<(string, DateTimeOffset)>();
    }
}

class MyInboxState : IInboxState
{
    public async Task<IReadOnlyList<(string MessageId, string Queue, DateTimeOffset ReceivedAt)>> GetStuckMessagesAsync(
        TimeSpan threshold, CancellationToken cancellationToken)
    {
        // TODO: Query your inbox store for stuck messages
        return Array.Empty<(string, string, DateTimeOffset)>();
    }
}

class MyOutboxState : IOutboxState
{
    public async Task<IReadOnlyList<(string MessageId, string Queue, DateTimeOffset CreatedAt)>> GetStuckMessagesAsync(
        TimeSpan threshold, CancellationToken cancellationToken)
    {
        // TODO: Query your outbox store for stuck messages
        return Array.Empty<(string, string, DateTimeOffset)>();
    }
}

class MyProcessingState : IProcessingState
{
    public async Task<IReadOnlyList<(string ProcessorId, string Component, DateTimeOffset LastActivityAt)>> GetIdleProcessorsAsync(
        TimeSpan threshold, CancellationToken cancellationToken)
    {
        // TODO: Query your processor tracking for idle processors
        return Array.Empty<(string, string, DateTimeOffset)>();
    }
}
