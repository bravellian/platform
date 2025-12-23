# Bravellian.Platform.Modularity

Engine-first module infrastructure for transport-agnostic UI, webhook, and background workflows.

## Install

```bash
dotnet add package Bravellian.Platform.Modularity
```

## Usage

```csharp
ModuleRegistry.RegisterModule<MyModule>();

builder.Services.AddModuleServices(builder.Configuration);

builder.Services.AddSingleton<UiEngineAdapter>();
builder.Services.AddSingleton<WebhookEngineAdapter>();

builder.Services.AddSingleton<IRequiredServiceValidator, MyRequiredServiceValidator>();
builder.Services.AddSingleton<IWebhookSignatureValidator, MySignatureValidator>();
```

## Examples

### Module with UI engine

```csharp
public sealed class MyModule : IModuleDefinition
{
    public string Key => "my-module";
    public string DisplayName => "My Module";

    public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();
    public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

    public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
    {
    }

    public void AddModuleServices(IServiceCollection services)
    {
        services.AddSingleton<MyUiEngine>();
    }

    public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
    {
        builder.AddCheck("my-module", () => HealthCheckResult.Healthy());
    }

    public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
    {
        yield return new ModuleEngineDescriptor<IUiEngine<MyCommand, MyViewModel>>(
            Key,
            new ModuleEngineManifest(
                "ui.example",
                "1.0",
                "Example UI engine",
                EngineKind.Ui,
                Inputs: new[] { new ModuleEngineSchema("command", typeof(MyCommand)) },
                Outputs: new[] { new ModuleEngineSchema("viewModel", typeof(MyViewModel)) }),
            sp => sp.GetRequiredService<MyUiEngine>());
    }
}
```

### Background-only module

```csharp
public sealed class WorkerModule : IModuleDefinition
{
    public string Key => "worker";
    public string DisplayName => "Worker";

    public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();
    public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

    public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
    {
    }

    public void AddModuleServices(IServiceCollection services)
    {
        services.AddHostedService<WorkerService>();
    }

    public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
    {
        builder.AddCheck("worker", () => HealthCheckResult.Healthy());
    }

    public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        => Array.Empty<IModuleEngineDescriptor>();
}
```

### Typed UI endpoint handler

```csharp
app.MapPost("/modules/{moduleKey}/ui/{engineId}", async (
    string moduleKey,
    string engineId,
    MyCommand command,
    UiEngineAdapter adapter,
    CancellationToken cancellationToken) =>
{
    var response = await adapter.ExecuteAsync<MyCommand, MyViewModel>(
        moduleKey,
        engineId,
        command,
        cancellationToken);

    return Results.Ok(response.ViewModel);
});
```

## Documentation

- https://github.com/bravellian/platform
- docs/INDEX.md
- docs/modularity-quickstart.md
- docs/engine-overview.md
- docs/module-engine-architecture.md
