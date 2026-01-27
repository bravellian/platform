# Bravellian.Platform.Modularity.AspNetCore

Minimal API endpoint helpers for engine-first modules.

## Install

```bash
dotnet add package Bravellian.Platform.Modularity.AspNetCore
```

## Usage

```csharp
ModuleRegistry.RegisterModule<MyModule>();

builder.Services.AddModuleServices(builder.Configuration);
builder.Services.AddSingleton<UiEngineAdapter>();
builder.Services.AddBravellianWebhooks();
builder.Services.AddModuleWebhookProviders();

app.MapUiEngineEndpoints();
app.MapWebhookEngineEndpoints();
```

`MapUiEngineEndpoints` requires UI manifests to declare `Inputs` and `Outputs`.
`MapWebhookEngineEndpoints` uses the `Bravellian.Platform.Webhooks` ingestion pipeline and requires webhook metadata.
If engines declare required services, register `IRequiredServiceValidator` in DI.

## Examples

### Custom routes

```csharp
app.MapUiEngineEndpoints(options =>
{
    options.RoutePattern = "/modules/{moduleKey}/ui/{engineId}";
    options.InputSchemaName = "command";
    options.OutputSchemaName = "viewModel";
});

app.MapWebhookEngineEndpoints(options =>
{
    options.RoutePattern = "/hooks/{provider}/{eventType}";
});
```

Webhook ingestion uses the default pipeline responses (202 for accepted and 401/403 for rejected).

## Documentation

- https://github.com/bravellian/platform
- docs/modularity-quickstart.md
- docs/engine-overview.md
