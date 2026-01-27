# Bravellian.Platform.Metrics.HttpServer

Self-hosted Prometheus metrics server for Bravellian.Platform using the OpenTelemetry HTTP listener exporter.

## Install

```bash
dotnet add package Bravellian.Platform.Metrics.HttpServer
```

## Usage

```csharp
using Bravellian.Platform.Metrics.HttpServer;

using var server = new PlatformMetricsHttpServer(new PlatformMetricsHttpServerOptions
{
    Meter = new PlatformMeterOptions
    {
        MeterName = "Bravellian.Platform.MyApp"
    },
    UriPrefixes = ["http://localhost:9464/"],
    ScrapeEndpointPath = "/metrics"
});

Console.WriteLine("Prometheus scrape endpoint running at http://localhost:9464/metrics");
Console.ReadLine();
```
