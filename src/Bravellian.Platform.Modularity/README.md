# Bravellian.Platform.Modularity

Composable module infrastructure for ASP.NET Core and generic hosts.

## Overview

The modularity tooling is now offered as three focused packages so you only take the dependencies you need:

- **Bravellian.Platform.Modularity.Core** – background/headless modules for generic hosts, no ASP.NET Core dependency
- **Bravellian.Platform.Modularity.Api** – API-first modules that expose endpoints, depends on `Microsoft.AspNetCore.App`
- **Bravellian.Platform.Modularity.FullStack** – UI-enabled modules that include navigation and Razor Pages wiring

The existing `Bravellian.Platform.Modularity` package remains as a convenience meta-package that references all of the above and re-exports the familiar extension methods.

## Requirements

This library targets **.NET 10.0** and requires:
- .NET SDK 10.0 or later
- ASP.NET Core 10.0 (included in .NET 10.0 SDK)

### Why .NET 10?

This library uses features introduced in C# 13 and .NET 10, including:
- `System.Threading.Lock` - Modern lock type introduced in .NET 9, fully supported in .NET 10
- Enhanced nullability annotations
- Performance improvements in ASP.NET Core

If you need support for earlier .NET versions, please file an issue to discuss compatibility requirements.

## Getting Started

### 1. Register Module Types

```csharp
// In your Program.cs or Startup.cs
ModuleRegistry.RegisterBackgroundModule<MyBackgroundModule>();
ApiModuleRegistry.RegisterApiModule<MyApiModule>();
FullStackModuleRegistry.RegisterFullStackModule<MyFullStackModule>();
```

### 2. Configure Services

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add module services
builder.Services.AddBackgroundModuleServices(builder.Configuration);
builder.Services.AddApiModuleServices(builder.Configuration);
builder.Services.AddFullStackModuleServices(builder.Configuration);

// For Full Stack modules with Razor Pages
builder.Services.AddRazorPages()
    .ConfigureFullStackModuleRazorPages();

var app = builder.Build();
```

### 3. Map Module Endpoints

```csharp
// Map all module endpoints
app.MapModuleEndpoints();

app.Run();
```

## Module Implementation

### Background Module Example

```csharp
public class MyBackgroundModule : IBackgroundModule
{
    public string Key => "my-background";
    public string DisplayName => "My Background Service";

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
        builder.AddCheck("my-background", () => HealthCheckResult.Healthy());
    }
}
```

### API Module Example

```csharp
public class MyApiModule : IApiModule
{
    public string Key => "my-api";
    public string DisplayName => "My API";

    // ... configuration methods ...

    public void MapApiEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/status", () => Results.Ok(new { Status = "OK" }));
        group.MapPost("/data", (DataRequest request) => Results.Created());
    }
}
```

### Full Stack Module Example

```csharp
public class MyFullStackModule : IFullStackModule, INavigationModuleMetadata
{
    public string Key => "my-module";
    public string DisplayName => "My Module";
    public string AreaName => "MyModule";
    public string NavigationGroup => "Tools";
    public int NavigationOrder => 10;

    // ... configuration and service methods ...

    public void ConfigureRazorPages(RazorPagesOptions options)
    {
        options.Conventions.AuthorizeAreaFolder(AreaName, "/");
    }

    public IEnumerable<ModuleNavLink> GetNavLinks()
    {
        yield return ModuleNavLink.Create("Dashboard", "/dashboard", 0, "home");
        yield return ModuleNavLink.Create("Settings", "/settings", 10, "settings");
    }

    public void MapApiEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/api/data", () => Results.Ok());
    }
}
```

## Features

### Configuration Management

Modules declare their required and optional configuration keys. The registry validates that all required configuration is present before initialization.

### Health Checks

All modules can register health checks that integrate with ASP.NET Core's health check system.

### Navigation Composition

Full Stack modules can contribute navigation links that are automatically composed into a unified navigation structure.

### Thread Safety

The module registry is thread-safe and can be used from multiple threads simultaneously.

### Validation

The system validates:
- Module keys are URL-safe (no slashes)
- Module keys are unique (case-insensitive)
- Modules are not registered in multiple categories
- Required configuration is present

## License

Copyright (c) Bravellian

Licensed under the Apache License, Version 2.0. See the LICENSE file for details.
