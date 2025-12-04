# Bravellian Code Generators

This directory contains Roslyn source generators that automatically create strongly-typed wrappers for common value types.

## Supported Generators

- **StringBackedType** - String-backed value types with optional validation
- **GuidBackedType** - GUID-backed value types  
- **FastIdBackedType** - Fast ID types
- **NumberBackedEnum** - Number-based enumeration types
- **StringBackedEnum** - String-based enumeration types
- **MultiValueBackedType** - Multi-value types
- **GenericBackedType** - Generic value types
- **DtoEntity** - DTO and entity types

## Configuration

### License Headers

You can customize the license header for generated code by setting the `GeneratedCodeLicenseHeader` MSBuild property in your project file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    
    <!-- Custom license header for generated code -->
    <GeneratedCodeLicenseHeader>
// Copyright (c) Your Company Name
// Licensed under MIT License
    </GeneratedCodeLicenseHeader>
  </PropertyGroup>
</Project>
```

If not specified, the generators will not include a license header.

### Generated Code Characteristics

All generated types are:
- **Partial** - Allowing you to extend them in separate files
- **Strongly-typed** - Providing compile-time safety
- **JSON-serializable** - With built-in JsonConverter
- **Type-convertible** - With built-in TypeConverter

Generated types implement standard .NET interfaces:
- `IComparable` and `IComparable<T>`
- `IEquatable<T>`
- `IParsable<T>` (where applicable)
- `ISpanParsable<T>` (for GUID types)

### Extending Generated Types

Since all types are generated as `partial`, you can easily extend them:

```csharp
// Generated (automatic)
public readonly partial record struct CustomerId
{
    public Guid Value { get; init; }
    // ... generated members
}

// Your extension (manual - in a separate file)
public readonly partial record struct CustomerId
{
    public static CustomerId FromLegacyId(int legacyId)
    {
        // Custom conversion logic
        return new CustomerId(ConvertToGuid(legacyId));
    }
    
    public bool IsSystemId() => Value == Guid.Empty;
}
```

## Example Usage

### String-Backed Type

Create a file named `EmailAddress.string.json`:

```json
{
  "name": "EmailAddress",
  "namespace": "MyApp.Domain",
  "regex": "^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$"
}
```

This generates a validated email address type:

```csharp
var email = EmailAddress.Parse("user@example.com");
Console.WriteLine(email.Value); // "user@example.com"

// Validation is automatic
try 
{
    var invalid = EmailAddress.Parse("not-an-email");
}
catch (ArgumentOutOfRangeException)
{
    // Invalid email format
}
```

### GUID-Backed Type

Create a file named `UserId.guid.json`:

```json
{
  "name": "UserId",
  "namespace": "MyApp.Domain"
}
```

This generates a strongly-typed user ID:

```csharp
var userId = UserId.GenerateNew();
var empty = UserId.Empty;
var fromGuid = UserId.From(Guid.NewGuid());
```

### Number-Backed Enum

Create a file named `OrderStatus.enum.json`:

```json
{
  "name": "OrderStatus",
  "namespace": "MyApp.Domain",
  "numberType": "int",
  "values": [
    { "name": "Pending", "value": "0", "displayName": "Pending" },
    { "name": "Processing", "value": "1", "displayName": "Processing" },
    { "name": "Completed", "value": "2", "displayName": "Completed" }
  ]
}
```

## Error Messages

The generators provide detailed error messages with context:

- **BG001**: General generator error with exception details
- **BG002**: File skipped with reason
- **BG003**: Duplicate generated file name detected
- **BG004**: Validation error with item name and context
- **BG005**: Missing required property with property name

Each error includes the file path and helpful context to identify and fix the issue.

## Best Practices

1. **License Headers**: Set the license header at the project level in your .csproj file
2. **Partial Classes**: Use partial classes to extend generated types without modifying generated code
3. **Naming**: Follow C# naming conventions for type names in your JSON files
4. **Organization**: Keep generator JSON files co-located with related code

## Troubleshooting

### Generator not running

Ensure your project file includes the generator package:

```xml
<ItemGroup>
  <PackageReference Include="Bravellian.Generators" Version="x.x.x" />
</ItemGroup>
```

### Missing generated files

Check that your JSON files match the expected naming pattern (e.g., `*.string.json`, `*.guid.json`)

### Compilation errors in generated code

If you see compilation errors in generated files:
1. Clean and rebuild the solution
2. Check that all required dependencies are installed
3. Verify your JSON configuration files are valid

## Support

For issues or questions, please file an issue on the GitHub repository.
