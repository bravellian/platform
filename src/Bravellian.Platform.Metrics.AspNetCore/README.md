# Bravellian.Platform.Metrics.AspNetCore

ASP.NET Core integration for Bravellian.Platform metrics using OpenTelemetry and Prometheus.

## Install

```bash
dotnet add package Bravellian.Platform.Metrics.AspNetCore
```

## Usage

```csharp
using Bravellian.Platform.Metrics.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformMetrics(options =>
{
    options.EnablePrometheusExporter = true;
    options.PrometheusEndpointPath = "/metrics";
    options.Meter.MeterName = "Bravellian.Platform.MyApp";
});

var app = builder.Build();

app.MapPlatformMetricsEndpoint();

app.Run();
```
