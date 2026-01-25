<!--
Integration summary:
1) Register once: builder.Services.AddBravellianHealthProbe(options => { options.BaseUrl = new Uri("https://localhost:5001"); options.DefaultEndpoint = "ready"; options.Endpoints["ready"] = "/ready"; });
2) Early in Main: var exitCode = await HealthProbeApp.TryRunHealthCheckAndExitAsync(args, app.Services, app.Lifetime.ApplicationStopping); if (exitCode >= 0) return exitCode;
-->
# Bravellian.Platform.HealthProbe

HealthProbe adds a small healthcheck CLI to your app so `healthcheck` requests hit your configured endpoint and exit with a Docker-friendly status code.

## Quick start (minimal hosting)

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddBravellianHealthProbe(options =>
{
    options.BaseUrl = new Uri("https://localhost:5001");
    options.DefaultEndpoint = "ready";
    options.Endpoints["ready"] = "/ready";
    options.Endpoints["deploy"] = "/health/deploy";
    options.ApiKey = builder.Configuration["HealthProbe:ApiKey"];
});

var app = builder.Build();

var exitCode = await HealthProbeApp.TryRunHealthCheckAndExitAsync(
    args,
    app.Services,
    app.Lifetime.ApplicationStopping);

if (exitCode >= 0)
{
    return exitCode;
}

await app.RunAsync();
```

## Configuration keys (optional)

You can configure via `IConfiguration` (or override via code). These keys are optional:

- `Bravellian:HealthProbe:BaseUrl`
- `Bravellian:HealthProbe:DefaultEndpoint`
- `Bravellian:HealthProbe:Endpoints:<name>` (one or more relative paths)
- `Bravellian:HealthProbe:TimeoutSeconds`
- `Bravellian:HealthProbe:ApiKey`
- `Bravellian:HealthProbe:ApiKeyHeaderName` (defaults to `X-Api-Key`)

## URL + path rules

- If the configured endpoint value is an absolute URL, it is used as-is.
- If the endpoint value is a relative path, it is combined with `BaseUrl`.

If `DefaultEndpoint` is not set and only one endpoint is configured, that endpoint is used by default.

## CLI usage

```
healthcheck            # uses DefaultEndpoint
healthcheck ready
healthcheck deploy

--url <url>            # override configured URL
--timeout <seconds>    # override timeout in seconds
--header <name>        # override API key header name
--apikey <value>       # override API key value
--insecure             # allow invalid TLS certs (off by default)
--json                 # output JSON instead of a single line
```

Exit codes:
- 0 = healthy
- 1 = unhealthy response
- 2 = invalid arguments or missing URL
- 3 = exception / timeout / network error

## Deploy-time usage

After deployment, invoke the app binary with `healthcheck` to probe the configured endpoint:

```bash
./MyApp healthcheck ready
./MyApp healthcheck deploy --json
./MyApp healthcheck ready --url https://service.example.com/ready --timeout 2
```

If you want to avoid passing flags, set configuration at deploy time using environment variables:

```bash
Bravellian__HealthProbe__BaseUrl=https://service.example.com
Bravellian__HealthProbe__DefaultEndpoint=ready
Bravellian__HealthProbe__Endpoints__ready=/ready
Bravellian__HealthProbe__Endpoints__deploy=/health/deploy
Bravellian__HealthProbe__TimeoutSeconds=2
Bravellian__HealthProbe__ApiKey=super-secret
Bravellian__HealthProbe__ApiKeyHeaderName=X-Api-Key
```

## Docker HEALTHCHECK example

In containers, point `BaseUrl` at the app’s local listener (typically `http://localhost:<port>`). If you do not configure it in code, set it with environment variables in the Dockerfile (or your orchestrator).

```dockerfile
ENV Bravellian__HealthProbe__BaseUrl=http://localhost:8080
ENV Bravellian__HealthProbe__DefaultEndpoint=ready
ENV Bravellian__HealthProbe__Endpoints__ready=/ready

HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD ["./MyApp", "healthcheck", "ready"]
```

## Security note

`--insecure` disables TLS certificate validation. It is intended for local development only.
