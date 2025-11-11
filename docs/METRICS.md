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

## Examples

See the [examples directory](../examples/metrics/) for complete working examples.

## License

Copyright (c) Bravellian. Licensed under the Apache License 2.0.
