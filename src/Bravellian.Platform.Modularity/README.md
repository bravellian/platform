# Bravellian.Platform.Modularity

Engine-first module infrastructure for generic hosts and adapters.

## Overview

`Bravellian.Platform.Modularity` provides a single, transport-agnostic module system. Modules register services, configuration, and health checks, and optionally expose engines (UI/webhook) through manifests and descriptors. Hosts choose how to surface engines via adapters.

Optional adapters live in separate packages:
- **Bravellian.Platform.Modularity.Razor** – Razor Pages adapter for UI engines.
- **Bravellian.Platform.Modularity.AspNetCore** – Minimal API endpoint helpers for UI and webhook engines.

## Requirements

This library targets **.NET 10.0** and requires:
- .NET SDK 10.0 or later

## Getting Started

### 1. Register Module Types

```csharp
// In your Program.cs or Startup.cs
ModuleRegistry.RegisterModule<MyModule>();
```

### 2. Configure Services

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add module services
builder.Services.AddModuleServices(builder.Configuration);
builder.Services.AddSingleton<UiEngineAdapter>();
builder.Services.AddSingleton<WebhookEngineAdapter>();
builder.Services.AddSingleton<IWebhookSignatureValidator, MySignatureValidator>();
builder.Services.AddSingleton<IRequiredServiceValidator, MyRequiredServiceValidator>();

// Optional Razor Pages adapter
builder.Services.AddRazorPages()
    .ConfigureRazorModulePages();

var app = builder.Build();
```

### 3. Wire Adapters

```csharp
app.MapUiEngineEndpoints();
app.MapWebhookEngineEndpoints();
```

## Module Implementation

Modules implement `IModuleDefinition` and return engine descriptors when applicable.

```csharp
public sealed class MyModule : IModuleDefinition
{
    public string Key => "my-module";
    public string DisplayName => "My Module";

    public IEnumerable<string> GetRequiredConfigurationKeys()
    {
        yield return "MyModule:ApiKey";
    }

    public IEnumerable<string> GetOptionalConfigurationKeys()
    {
        yield return "MyModule:Timeout";
    }

    public void LoadConfiguration(
        IReadOnlyDictionary<string, string> required,
        IReadOnlyDictionary<string, string> optional)
    {
        // Store configuration for use during service registration
    }

    public void AddModuleServices(IServiceCollection services)
    {
        services.AddHostedService<MyBackgroundService>();
    }

    public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
    {
        builder.AddCheck("my-module", () => HealthCheckResult.Healthy());
    }

    public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
    {
        yield return new ModuleEngineDescriptor<IUiEngine<LoginCommand, LoginViewModel>>(
            Key,
            new ModuleEngineManifest(
                "ui.login",
                "1.0",
                "Login UI engine",
                EngineKind.Ui),
            sp => sp.GetRequiredService<LoginUiEngine>());
    }
}
```

## Features

### Configuration Management

Modules declare their required and optional configuration keys. The registry validates that all required configuration is present before initialization.

### Health Checks

All modules can register health checks that integrate with ASP.NET Core's health check system.

### Engine Discovery

Engines are registered with the `ModuleEngineDiscoveryService`, which supports querying by kind or feature area and resolving descriptors for adapters.

### Validation

The system validates:
- Module keys are URL-safe (no slashes)
- Module keys are unique (case-insensitive)
- Required configuration is present
- Engine descriptors match their owning module key

## More documentation

- [Modularity Quick Start](../../docs/modularity-quickstart.md)
- [Engine Contracts Overview](../../docs/engine-overview.md)
- [Module Engine Architecture](../../docs/module-engine-architecture.md)

## License

Copyright (c) Bravellian

Licensed under the Apache License, Version 2.0. See the LICENSE file for details.
