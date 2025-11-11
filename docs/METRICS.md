# Platform Metrics Guide

## Overview

The Bravellian Platform provides a reusable metrics substrate that enables applications to emit and persist metrics at both the platform and application levels. Metrics are stored per-tenant with minute-level granularity and optionally aggregated centrally at hourly intervals for cross-tenant analysis.

## Features

- **Per-tenant minute buckets** stored in tenant databases
- **Optional central hourly rollups** for cross-tenant views
- **Support for platform and app-defined metrics** without schema changes
- **Multi-instance safe** with InstanceId-aware additive upserts
- **Controlled cardinality** via tag whitelists and registration
- **Integration with .NET Meter** sources for automatic metric collection
- **Built-in retention management** with configurable retention periods
- **Automatic platform metrics** registered when metrics exporter is enabled
- **Type-safe metric definitions** with enum-based aggregation kinds and standard units

## Quick Start

### 1. Add Metrics to Your Application

```csharp
using Bravellian.Platform.Metrics;

// In your Startup.cs or Program.cs
builder.Services.AddMetricsExporter(options =>
{
    options.Enabled = true;
    options.ServiceName = "MyService";
    options.EnableCentralRollup = true;
    options.CentralConnectionString = builder.Configuration.GetConnectionString("Central");
    options.FlushInterval = TimeSpan.FromSeconds(60);
    options.MinuteRetentionDays = 14;
    options.HourlyRetentionDays = 90;
});

// Add health check (optional)
builder.Services.AddMetricsExporterHealthCheck();

// Platform metrics are automatically registered!
```

### 2. Register Application-Specific Metrics

```csharp
// Get the registrar from DI
var registrar = serviceProvider.GetRequiredService<IMetricRegistrar>();

// Register custom metrics using type-safe enums
registrar.Register(new MetricRegistration(
    "app.orders.created.count",
    MetricUnit.Count,
    MetricAggregationKind.Counter,
    "Number of orders created",
    new[] { "source", "region" }));

registrar.Register(new MetricRegistration(
    "app.order.processing_time.ms",
    MetricUnit.Milliseconds,
    MetricAggregationKind.Histogram,
    "Time to process an order",
    new[] { "order_type" }));
```

### 3. Emit Metrics Using .NET Diagnostics

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class OrderService
{
    private static readonly Meter _meter = new("Bravellian.Platform.MyApp");
    private static readonly Counter<long> _ordersCreated = 
        _meter.CreateCounter<long>("app.orders.created.count");
    private static readonly Histogram<double> _processingTime = 
        _meter.CreateHistogram<double>("app.order.processing_time.ms");

    public async Task CreateOrderAsync(Order order)
    {
        var sw = Stopwatch.StartNew();
        
        // ... create order logic ...
        
        _ordersCreated.Add(1, 
            new KeyValuePair<string, object?>("source", order.Source),
            new KeyValuePair<string, object?>("region", order.Region));
            
        _processingTime.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("order_type", order.Type));
    }
}
```

## Metric Types

### Counter
Monotonically increasing values (e.g., request counts, error counts).
- **Unit**: typically `MetricUnit.Count`
- **AggKind**: `MetricAggregationKind.Counter`
- **Example**: `outbox.published.count`, `app.orders.created.count`

### Gauge
Point-in-time values that can go up or down (e.g., queue depth, temperature).
- **Unit**: varies (`MetricUnit.Count`, `MetricUnit.Percent`, etc.)
- **AggKind**: `MetricAggregationKind.Gauge`
- **Example**: `outbox.pending.count`, `dlq.depth`

### Histogram
Distribution of values (e.g., latencies, sizes).
- **Unit**: typically `MetricUnit.Milliseconds` or `MetricUnit.Seconds`
- **AggKind**: `MetricAggregationKind.Histogram`
- **Example**: `outbox.publish_latency.ms`, `app.order.processing_time.ms`

## Standard Metric Units

The platform provides standard unit constants in `MetricUnit`:

- `MetricUnit.Count` - Dimensionless count
- `MetricUnit.Milliseconds` - Time in milliseconds
- `MetricUnit.Seconds` - Time in seconds
- `MetricUnit.Bytes` - Data size in bytes
- `MetricUnit.Percent` - Percentage (0-100)

## Aggregation Kinds

Use the `MetricAggregationKind` enum for type-safe metric definitions:

- `MetricAggregationKind.Counter` - Monotonically increasing values
- `MetricAggregationKind.Gauge` - Point-in-time sampled values
- `MetricAggregationKind.Histogram` - Distribution of values with percentiles

## Platform Metrics Catalog

**Platform metrics are automatically registered** when you call `AddMetricsExporter()`. The platform includes 15 predefined metrics for core functionality:

### Outbox Metrics
- `outbox.published.count` - Messages published
- `outbox.pending.count` - Pending messages
- `outbox.oldest_age.seconds` - Age of oldest pending message
- `outbox.publish_latency.ms` - Publishing latency

### Inbox Metrics
- `inbox.processed.count` - Messages processed
- `inbox.retry.count` - Message retries
- `inbox.failed.count` - Permanently failed messages
- `inbox.processing_latency.ms` - Processing latency

### DLQ Metrics
- `dlq.depth` - Messages in dead letter queue
- `dlq.oldest_age.seconds` - Age of oldest DLQ message

### Scheduler Metrics
- `scheduler.job.executed.count` - Jobs executed
- `scheduler.job.latency.ms` - Job execution time

### Lease Metrics
- `lease.acquired.count` - Leases acquired
- `lease.active.count` - Currently active leases

## Tag Guidelines

### Allowed Tags

Tags enable filtering and grouping of metrics. Each metric defines its allowed tags to control cardinality.

#### Global Allowed Tags (Apply to All Metrics)
- `event_type` - Type of event
- `service` - Service name
- `database_id` - Tenant/database identifier
- `topic` - Message topic
- `queue` - Queue name
- `result` - Operation result (success/failure)
- `reason` - Failure reason
- `kind` - Resource kind
- `job_name` - Scheduled job name
- `resource` - Resource name

You can customize global allowed tags:

```csharp
builder.Services.AddMetricsExporter(options =>
{
    options.GlobalAllowedTags = new HashSet<string>(StringComparer.Ordinal)
    {
        "service",
        "environment",
        "region",
        "custom_tag"
    };
});
```

### Best Practices

1. **Keep cardinality low**: Avoid high-cardinality tags (e.g., user IDs, transaction IDs)
2. **Use meaningful names**: Tags should be descriptive (e.g., `order_type` not `ot`)
3. **Be consistent**: Use the same tag names across related metrics
4. **Limit tag count**: Keep to 3-5 tags per metric
5. **Whitelist tags**: Always register metrics with their allowed tags

### Bad Examples (High Cardinality)
```csharp
// DON'T: User ID as a tag (millions of unique values)
_counter.Add(1, new KeyValuePair<string, object?>("user_id", userId));

// DON'T: Timestamp as a tag
_counter.Add(1, new KeyValuePair<string, object?>("timestamp", DateTime.UtcNow.ToString()));

// DON'T: Request ID as a tag
_counter.Add(1, new KeyValuePair<string, object?>("request_id", requestId));
```

### Good Examples (Low Cardinality)
```csharp
// DO: Category or type as a tag (limited unique values)
_counter.Add(1, new KeyValuePair<string, object?>("order_type", "standard"));

// DO: Region as a tag (limited unique values)
_counter.Add(1, new KeyValuePair<string, object?>("region", "us-west"));

// DO: Status as a tag (limited unique values)
_counter.Add(1, new KeyValuePair<string, object?>("status", "completed"));
```

## Database Schema

### Tenant Databases (Minute Data)

- `infra.MetricDef` - Metric definitions
- `infra.MetricSeries` - Time series identities (MetricDefId + Service + InstanceId + Tags)
- `infra.MetricPointMinute` - Minute-level data points

### Central Database (Hourly Rollups)

- `infra.MetricDef` - Metric definitions
- `infra.MetricSeries` - Time series identities with `DatabaseId` for tenant
- `infra.MetricPointHourly` - Hourly aggregated data points (columnstore)
- `infra.ExporterHeartbeat` - Exporter health tracking

## Configuration

### appsettings.json Example

```json
{
  "MetricsExporter": {
    "Enabled": true,
    "ServiceName": "MyApplication",
    "EnableCentralRollup": true,
    "CentralConnectionString": "Server=central;Database=Platform;...",
    "FlushInterval": "00:01:00",
    "ReservoirSize": 2000,
    "MinuteRetentionDays": 14,
    "HourlyRetentionDays": 90
  }
}
```

### Options Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | true | Enable/disable metrics collection |
| `ServiceName` | string | "Unknown" | Name of the service emitting metrics |
| `EnableCentralRollup` | bool | true | Enable hourly rollups to central database |
| `CentralConnectionString` | string? | null | Connection string for central database |
| `FlushInterval` | TimeSpan | 60s | How often to flush metrics to database |
| `ReservoirSize` | int | 1000 | Reservoir size for percentile calculation |
| `MinuteRetentionDays` | int | 14 | Days to retain minute-level data |
| `HourlyRetentionDays` | int | 90 | Days to retain hourly-level data |
| `GlobalAllowedTags` | HashSet<string> | See above | Global tag whitelist |

## Querying Metrics

### Query Minute Data (Tenant Database)

```sql
-- Get minute-level metrics for the last hour
SELECT 
    md.Name,
    ms.Service,
    ms.InstanceId,
    ms.TagsJson,
    mp.BucketStartUtc,
    mp.ValueSum,
    mp.ValueCount,
    mp.ValueMin,
    mp.ValueMax,
    mp.P95,
    mp.P99
FROM infra.MetricPointMinute mp
JOIN infra.MetricSeries ms ON mp.SeriesId = ms.SeriesId
JOIN infra.MetricDef md ON ms.MetricDefId = md.MetricDefId
WHERE mp.BucketStartUtc >= DATEADD(HOUR, -1, GETUTCDATE())
  AND md.Name = 'outbox.published.count'
ORDER BY mp.BucketStartUtc DESC;
```

### Query Hourly Data (Central Database)

```sql
-- Get hourly aggregates across all tenants
SELECT 
    md.Name,
    ms.DatabaseId,
    ms.Service,
    mp.BucketStartUtc,
    SUM(mp.ValueSum) as TotalValue,
    SUM(mp.ValueCount) as TotalCount,
    MAX(mp.P95) as MaxP95
FROM infra.MetricPointHourly mp
JOIN infra.MetricSeries ms ON mp.SeriesId = ms.SeriesId
JOIN infra.MetricDef md ON ms.MetricDefId = md.MetricDefId
WHERE mp.BucketStartUtc >= DATEADD(DAY, -7, GETUTCDATE())
  AND md.Name = 'inbox.processing_latency.ms'
GROUP BY md.Name, ms.DatabaseId, ms.Service, mp.BucketStartUtc
ORDER BY mp.BucketStartUtc DESC;
```

## Retention Management

The platform automatically manages data retention through scheduled jobs:

- **MetricsRetentionMinuteJob**: Deletes minute data older than `MinuteRetentionDays`
- **MetricsRetentionHourlyJob**: Deletes hourly data older than `HourlyRetentionDays`

## Health Monitoring

The metrics exporter includes a health check that reports:
- Last successful flush timestamp
- Time since last flush
- Any error messages

Access via `/health` endpoint when health checks are configured.

## Multi-Instance Considerations

When running multiple instances of an application:

1. **Counters**: Values are summed across instances
2. **Gauges**: Last recorded value wins
3. **Histograms**: Use MAX(P95) or MAX(P99) across instances for SLOs

Each instance has a unique `InstanceId` allowing you to query per-instance metrics when needed.

## Troubleshooting

### Metrics Not Appearing

1. Check that `Enabled = true` in configuration
2. Verify database schema is deployed
3. Check exporter health status
4. Review application logs for errors
5. Verify meter name starts with "Bravellian.Platform"

### High Database Growth

1. Reduce `MinuteRetentionDays` and `HourlyRetentionDays`
2. Review tag cardinality (avoid high-cardinality tags)
3. Consider disabling central rollups if not needed
4. Check retention jobs are running

### Missing Percentiles

Percentiles (P50, P95, P99) are calculated per-instance using reservoir sampling. When combining data from multiple instances, use MAX aggregation for P95/P99 values.

## Practical Usage Guide

### When to Use Metrics

Use metrics to track:

1. **Operational Health**
   - Request rates and error rates
   - Queue depths and processing latencies
   - Resource utilization (connections, memory pressure indicators)

2. **Business KPIs**
   - Orders processed, payments completed
   - User signups, feature adoption rates
   - Revenue-impacting operations

3. **Performance Monitoring**
   - API endpoint latencies (p95, p99)
   - Database query times
   - External service call durations

4. **Debugging & Diagnostics**
   - Error counts by type/category
   - Retry attempts and failure reasons
   - Background job execution rates

### What Metrics to Capture

#### Essential Application Metrics

**For API/Web Applications:**
```csharp
// Request throughput and errors
new MetricRegistration(
    "app.http.requests.count",
    MetricUnit.Count,
    MetricAggregationKind.Counter,
    "HTTP requests received",
    new[] { "endpoint", "method", "status_code" });

// Request latency
new MetricRegistration(
    "app.http.request_duration.ms",
    MetricUnit.Milliseconds,
    MetricAggregationKind.Histogram,
    "HTTP request duration",
    new[] { "endpoint", "method" });
```

**For Background Workers:**
```csharp
// Job execution
new MetricRegistration(
    "app.worker.jobs.processed.count",
    MetricUnit.Count,
    MetricAggregationKind.Counter,
    "Background jobs processed",
    new[] { "job_type", "result" });

// Job duration
new MetricRegistration(
    "app.worker.job_duration.ms",
    MetricUnit.Milliseconds,
    MetricAggregationKind.Histogram,
    "Background job execution time",
    new[] { "job_type" });

// Queue depth (if applicable)
new MetricRegistration(
    "app.worker.queue.depth",
    MetricUnit.Count,
    MetricAggregationKind.Gauge,
    "Pending jobs in queue",
    new[] { "queue_name" });
```

**For Data Processing:**
```csharp
// Records processed
new MetricRegistration(
    "app.etl.records.processed.count",
    MetricUnit.Count,
    MetricAggregationKind.Counter,
    "ETL records processed",
    new[] { "source", "result" });

// Batch size
new MetricRegistration(
    "app.etl.batch_size",
    MetricUnit.Count,
    MetricAggregationKind.Histogram,
    "Number of records per batch",
    new[] { "source" });
```

#### Business Metrics

```csharp
// Orders
new MetricRegistration(
    "app.orders.created.count",
    MetricUnit.Count,
    MetricAggregationKind.Counter,
    "Orders created",
    new[] { "order_type", "payment_method" });

new MetricRegistration(
    "app.orders.value",
    MetricUnit.Count, // Could use a custom unit like "cents"
    MetricAggregationKind.Counter,
    "Order value in cents",
    new[] { "order_type", "currency" });

// User activity
new MetricRegistration(
    "app.users.active.count",
    MetricUnit.Count,
    MetricAggregationKind.Gauge,
    "Currently active users",
    new[] { "tenant_id" });
```

### How to Integrate Custom Metrics

#### Step-by-Step Integration

**1. Define Your Metrics at Startup**

Create a dedicated class for your metric definitions:

```csharp
// Metrics/ApplicationMetrics.cs
public static class ApplicationMetrics
{
    public static IReadOnlyList<MetricRegistration> All => new[]
    {
        new MetricRegistration(
            "app.orders.created.count",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Number of orders created",
            new[] { "order_type", "payment_method", "region" }),
            
        new MetricRegistration(
            "app.orders.processing_time.ms",
            MetricUnit.Milliseconds,
            MetricAggregationKind.Histogram,
            "Order processing duration",
            new[] { "order_type" }),
            
        // Add more metrics...
    };
}
```

**2. Register Metrics After Building the App**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add metrics exporter (platform metrics auto-registered)
builder.Services.AddMetricsExporter(options =>
{
    options.ServiceName = "OrderService";
    options.EnableCentralRollup = true;
    options.CentralConnectionString = builder.Configuration.GetConnectionString("Central");
});

var app = builder.Build();

// Register custom application metrics
var registrar = app.Services.GetRequiredService<IMetricRegistrar>();
registrar.RegisterRange(ApplicationMetrics.All);

app.Run();
```

**3. Emit Metrics in Your Services**

```csharp
// Services/OrderService.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class OrderService
{
    // Create a meter for your application
    private static readonly Meter _meter = new("Bravellian.Platform.OrderService");
    
    // Define instruments for your metrics
    private static readonly Counter<long> _ordersCreated = 
        _meter.CreateCounter<long>("app.orders.created.count");
    
    private static readonly Histogram<double> _orderProcessingTime = 
        _meter.CreateHistogram<double>("app.orders.processing_time.ms");
    
    private readonly ILogger<OrderService> _logger;
    
    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Your order creation logic
            var order = await ProcessOrderAsync(request);
            
            // Record successful order creation
            _ordersCreated.Add(1,
                new KeyValuePair<string, object?>("order_type", order.Type),
                new KeyValuePair<string, object?>("payment_method", order.PaymentMethod),
                new KeyValuePair<string, object?>("region", order.Region));
            
            // Record processing time
            _orderProcessingTime.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("order_type", order.Type));
            
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order");
            
            // Still record the attempt (with appropriate tags)
            _ordersCreated.Add(1,
                new KeyValuePair<string, object?>("order_type", request.Type),
                new KeyValuePair<string, object?>("payment_method", request.PaymentMethod),
                new KeyValuePair<string, object?>("region", request.Region),
                new KeyValuePair<string, object?>("result", "error"));
            
            throw;
        }
    }
    
    private async Task<Order> ProcessOrderAsync(CreateOrderRequest request)
    {
        // Implementation...
        await Task.Delay(100); // Simulate work
        return new Order();
    }
}
```

**4. Use Middleware for HTTP Metrics (Optional)**

```csharp
// Middleware/MetricsMiddleware.cs
public class MetricsMiddleware
{
    private static readonly Meter _meter = new("Bravellian.Platform.WebApp");
    private static readonly Counter<long> _httpRequests = 
        _meter.CreateCounter<long>("app.http.requests.count");
    private static readonly Histogram<double> _httpDuration = 
        _meter.CreateHistogram<double>("app.http.request_duration.ms");
    
    private readonly RequestDelegate _next;
    
    public MetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var endpoint = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        
        try
        {
            await _next(context);
        }
        finally
        {
            var statusCode = context.Response.StatusCode.ToString();
            
            _httpRequests.Add(1,
                new KeyValuePair<string, object?>("endpoint", endpoint),
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("status_code", statusCode));
            
            _httpDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("endpoint", endpoint),
                new KeyValuePair<string, object?>("method", method));
        }
    }
}

// Register in Program.cs
app.UseMiddleware<MetricsMiddleware>();
```

### Complete Example: E-Commerce Order Service

Here's a complete example showing metrics integration for an order processing service:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddMetricsExporter(options =>
{
    options.ServiceName = "ECommerceOrderService";
    options.EnableCentralRollup = true;
    options.CentralConnectionString = builder.Configuration.GetConnectionString("Central");
    options.MinuteRetentionDays = 7;
    options.HourlyRetentionDays = 90;
});

builder.Services.AddMetricsExporterHealthCheck();
builder.Services.AddSingleton<OrderService>();

var app = builder.Build();

// Register application metrics
var registrar = app.Services.GetRequiredService<IMetricRegistrar>();
registrar.RegisterRange(new[]
{
    new MetricRegistration(
        "app.orders.created.count",
        MetricUnit.Count,
        MetricAggregationKind.Counter,
        "Orders successfully created",
        new[] { "product_category", "payment_method" }),
        
    new MetricRegistration(
        "app.orders.failed.count",
        MetricUnit.Count,
        MetricAggregationKind.Counter,
        "Order creation failures",
        new[] { "failure_reason" }),
        
    new MetricRegistration(
        "app.inventory.check_duration.ms",
        MetricUnit.Milliseconds,
        MetricAggregationKind.Histogram,
        "Time to check inventory availability",
        new[] { "product_category" }),
});

app.MapHealthChecks("/health");
app.Run();
```

### Best Practices Summary

1. **Keep cardinality low**: Limit tag combinations to < 1000 per metric
2. **Use appropriate metric types**: Counters for totals, Gauges for current values, Histograms for distributions
3. **Tag wisely**: Use tags for filtering, not for unique identifiers
4. **Name consistently**: Use dots for namespacing (e.g., `app.orders.created.count`)
5. **Register early**: Define and register all metrics at application startup
6. **Don't over-instrument**: Focus on metrics that help you make decisions
7. **Use standard units**: Stick to `MetricUnit` constants when possible

## Examples

See the [examples directory](../examples/metrics/) for complete working examples.

## License

Copyright (c) Bravellian. Licensed under the Apache License 2.0.
