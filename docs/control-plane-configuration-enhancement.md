# Control Plane Configuration Enhancement

## Overview

This enhancement adds full configuration support for control plane database settings, including the ability to specify custom schema names. Previously, when using multi-database with control plane and discovery, there was no way to configure the control plane database schema name or other settings.

## Problem Solved

Before this change, the service collection extensions had the following limitations:

1. **No schema configuration for control plane**: When using `AddPlatformMultiDatabaseWithControlPlaneAndDiscovery` or `AddPlatformMultiDatabaseWithControlPlaneAndList`, the control plane database always used the default "dbo" schema.

2. **Limited configuration options**: Users couldn't specify additional control plane settings like schema deployment options in a structured way.

3. **Inflexible API**: The methods only accepted connection strings directly, making it difficult to extend with additional options in the future.

## Solution

### New `PlatformControlPlaneOptions` Class

A new configuration class has been introduced:

```csharp
public sealed class PlatformControlPlaneOptions
{
    public required string ConnectionString { get; init; }
    public string SchemaName { get; init; } = "dbo";
    public bool EnableSchemaDeployment { get; init; }
}
```

### Updated Method Signatures

Both control plane registration methods now have overloads that accept `PlatformControlPlaneOptions`:

```csharp
// Multi-database with list and control plane
IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndList(
    this IServiceCollection services,
    IEnumerable<PlatformDatabase> databases,
    PlatformControlPlaneOptions controlPlaneOptions)

// Multi-database with discovery and control plane
IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
    this IServiceCollection services,
    PlatformControlPlaneOptions controlPlaneOptions)
```

The old method signatures are still supported but marked as `[Obsolete]` to guide users to the new API.

## Usage Examples

### Example 1: Using Custom Schema for Control Plane

```csharp
services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
    new PlatformControlPlaneOptions
    {
        ConnectionString = "Server=localhost;Database=ControlPlane;",
        SchemaName = "platform_control",  // Custom schema name
        EnableSchemaDeployment = true
    });
```

### Example 2: Multi-Database with List and Custom Control Plane Schema

```csharp
var databases = new[]
{
    new PlatformDatabase
    {
        Name = "customer1",
        ConnectionString = "Server=localhost;Database=Customer1;",
        SchemaName = "app_data"
    },
    new PlatformDatabase
    {
        Name = "customer2",
        ConnectionString = "Server=localhost;Database=Customer2;",
        SchemaName = "app_data"
    }
};

services.AddPlatformMultiDatabaseWithControlPlaneAndList(
    databases,
    new PlatformControlPlaneOptions
    {
        ConnectionString = "Server=localhost;Database=ControlPlane;",
        SchemaName = "control_plane",  // Separate schema for control plane
        EnableSchemaDeployment = true
    });
```

### Example 3: Backward Compatibility (Old API)

The old API still works but will generate obsolete warnings:

```csharp
// This still works but is marked as obsolete
services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
    controlPlaneConnectionString: "Server=localhost;Database=ControlPlane;",
    enableSchemaDeployment: true);
// Schema defaults to "dbo"
```

## Benefits

1. **Full Control**: Users can now specify all control plane configuration options, including schema names.

2. **Consistency**: The control plane configuration follows the same pattern as `PlatformDatabase` objects, which already support schema names.

3. **Extensibility**: The options class can be easily extended with additional properties in the future without breaking existing code.

4. **Clarity**: The configuration structure makes it clear what settings apply to the control plane versus the application databases.

5. **Backward Compatibility**: Existing code continues to work with obsolete warnings that guide users to the new API.

## Internal Changes

### Semaphore Service Updates

The internal semaphore service registration has been updated to accept schema names:

```csharp
internal static void AddSemaphoreServices(
    this IServiceCollection services,
    string connectionString,
    string schemaName = "dbo",  // Now accepts schema name
    Action<SemaphoreOptions>? configure = null)
```

This ensures that semaphores (which run in the control plane) respect the configured schema name.

### Platform Configuration

The `PlatformConfiguration` class now stores the control plane schema name:

```csharp
internal sealed class PlatformConfiguration
{
    public string? ControlPlaneConnectionString { get; init; }
    public string? ControlPlaneSchemaName { get; init; }  // New property
    public bool EnableSchemaDeployment { get; init; }
    // ... other properties
}
```

## Testing

Comprehensive tests have been added to verify:

1. Schema name configuration is properly passed through to services
2. Default schema name ("dbo") is used when not specified
3. Backward compatibility with old API signatures
4. Semaphore services respect the control plane schema configuration

See `ControlPlaneConfigurationTests.cs` for the full test suite.
