# Modularity Quick Start

This guide shows how to wire engine-first modules into a host, expose UI and webhook engines, and optionally plug in Razor Pages.

## Install packages

```bash
# Core engine-first module runtime
dotnet add package Bravellian.Platform.Modularity

# Optional: minimal API endpoint helpers
dotnet add package Bravellian.Platform.Modularity.AspNetCore

# Optional: Razor Pages adapter
dotnet add package Bravellian.Platform.Modularity.Razor
```

## 1) Define a module and its engines

Modules implement `IModuleDefinition` and describe their engines using manifests and descriptors. Engines remain transport-agnostic.

```csharp
public sealed class OcrModule : IModuleDefinition
{
    public string Key => "ocr";
    public string DisplayName => "OCR";

    public IEnumerable<string> GetRequiredConfigurationKeys()
    {
        yield return "Ocr:ApiKey";
    }

    public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

    public void LoadConfiguration(
        IReadOnlyDictionary<string, string> required,
        IReadOnlyDictionary<string, string> optional)
    {
        // Store configuration values for later use.
    }

    public void AddModuleServices(IServiceCollection services)
    {
        services.AddSingleton<OcrUiEngine>();
        services.AddSingleton<OcrWebhookEngine>();
        services.AddHostedService<OcrWorker>();
    }

    public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
    {
        builder.AddCheck("ocr", () => HealthCheckResult.Healthy());
    }

    public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
    {
        yield return new ModuleEngineDescriptor<IUiEngine<OcrCommand, OcrViewModel>>(
            Key,
            new ModuleEngineManifest(
                "ui.ocr",
                "1.0",
                "OCR UI engine",
                EngineKind.Ui,
                Inputs: new[] { new ModuleEngineSchema("command", typeof(OcrCommand)) },
                Outputs: new[] { new ModuleEngineSchema("viewModel", typeof(OcrViewModel)) }),
            sp => sp.GetRequiredService<OcrUiEngine>());

        yield return new ModuleEngineDescriptor<IModuleWebhookEngine<OcrWebhookPayload>>(
            Key,
            new ModuleEngineManifest(
                "webhook.ocr",
                "1.0",
                "OCR webhook handler",
                EngineKind.Webhook,
                WebhookMetadata: new[]
                {
                    new ModuleEngineWebhookMetadata(
                        "acme",
                        "document.ready",
                        new ModuleEngineSchema("payload", typeof(OcrWebhookPayload)))
                }),
            sp => sp.GetRequiredService<OcrWebhookEngine>());
    }
}
```

Notes:
- `Inputs` and `Outputs` are required if you want to use generic UI endpoints.
- `WebhookMetadata` is required if you want to use generic webhook endpoints.

## 2) Register modules and core services

```csharp
ModuleRegistry.RegisterModule<OcrModule>();

builder.Services.AddModuleServices(builder.Configuration);

// Adapters / webhook pipeline
builder.Services.AddSingleton<UiEngineAdapter>();
builder.Services.AddBravellianWebhooks();
builder.Services.AddModuleWebhookProviders();

// Required when engines declare RequiredServices
builder.Services.AddSingleton<IRequiredServiceValidator, MyRequiredServiceValidator>();

// Required when webhook security is configured
builder.Services.AddSingleton<IModuleWebhookSignatureValidator, MySignatureValidator>();
```

`AddModuleServices` registers engine discovery and loads module configuration.

## 3) Expose endpoints (optional)

If you want endpoint helpers, use `Bravellian.Platform.Modularity.AspNetCore`.

```csharp
app.MapUiEngineEndpoints();
app.MapWebhookEngineEndpoints();
```

Customize route patterns or schema names:

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

If you need typed endpoints instead of generic ones, call `UiEngineAdapter` directly or use `WebhookEndpoint.HandleAsync` with the webhook pipeline.

## 4) Razor Pages adapter (optional)

Implement `IRazorModule` for modules that ship Razor Pages and add the adapter:

```csharp
public sealed class OcrRazorModule : OcrModule, IRazorModule
{
    public string AreaName => "Ocr";

    public void ConfigureRazorPages(RazorPagesOptions options)
    {
        options.Conventions.AuthorizeAreaFolder(AreaName, "/");
    }
}
```

```csharp
builder.Services.AddRazorPages()
    .ConfigureRazorModulePages();
```

Razor Pages can call `UiEngineAdapter` to invoke engines without binding to HTTP endpoints.

## 5) Background-only modules

Modules that only register services can return no descriptors:

```csharp
public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
    => Array.Empty<IModuleEngineDescriptor>();
```

## Testing notes

`ModuleRegistry` and `ModuleEngineRegistry` are global. Call `Reset()` in tests and avoid parallel tests that mutate registry state.
