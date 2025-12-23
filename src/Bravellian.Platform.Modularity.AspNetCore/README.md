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
builder.Services.AddSingleton<WebhookEngineAdapter>();

app.MapUiEngineEndpoints();
app.MapWebhookEngineEndpoints();
```

`MapUiEngineEndpoints` requires UI manifests to declare `Inputs` and `Outputs`.
`MapWebhookEngineEndpoints` requires webhook metadata with payload schemas.
If engines declare required services or webhook security, register
`IRequiredServiceValidator` and `IWebhookSignatureValidator` in DI.

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
    options.SignatureHeaderName = "X-Signature";
});
```

### Custom response mapping

```csharp
app.MapWebhookEngineEndpoints(options =>
{
    options.ResponseFactory = response =>
    {
        var statusCode = response.Outcome switch
        {
            WebhookOutcomeType.Acknowledge => StatusCodes.Status200OK,
            WebhookOutcomeType.EnqueueEvent => StatusCodes.Status202Accepted,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        return Results.Json(new { response.Outcome, response.Reason }, statusCode: statusCode);
    };
});
```

## Documentation

- https://github.com/bravellian/platform
- docs/modularity-quickstart.md
- docs/engine-overview.md
