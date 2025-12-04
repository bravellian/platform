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

By default, all generated code includes an Apache 2.0 license header. You can customize this by setting the `GeneratorConfig.CustomLicenseHeader` property.

```csharp
// Set a custom license header (typically in an assembly-level file)
using Bravellian.Generators;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("YourProject")]

// In a static constructor or module initializer:
GeneratorConfig.CustomLicenseHeader = @"
Copyright (c) Your Company Name
Licensed under MIT License
See LICENSE for details
";
```

The license header will automatically be formatted with `//` comment prefixes if not already present.

### Interface Generation

By default, generated types implement Bravellian-specific interfaces (`IHasValueConverter`, `IBgParsable<T>`, etc.) in addition to standard .NET interfaces. You can control this behavior:

```csharp
using Bravellian.Generators;

// Disable Bravellian-specific interfaces (use only standard .NET interfaces)
GeneratorConfig.IncludeBravellianInterfaces = false;
```

When set to `false`, only standard interfaces like `IComparable<T>`, `IEquatable<T>`, `IParsable<T>` are included.

### Partial Classes

By default, all generated types are `partial`, allowing you to extend them in separate files:

```csharp
// Generated code (automatic)
public readonly partial record struct CustomerId
{
    public Guid Value { get; init; }
    // ... generated members
}

// Your extension (manual)
public readonly partial record struct CustomerId
{
    public static CustomerId FromLegacyId(int legacyId)
    {
        // Custom conversion logic
        return new CustomerId(ConvertLegacyId(legacyId));
    }
}
```

To disable partial classes:

```csharp
GeneratorConfig.GenerateAsPartialClasses = false;
```

### Using Partial Classes for Custom Interfaces

If you want to implement additional interfaces without modifying the generated code, keep `GenerateAsPartialClasses = true` (default) and create a companion partial class:

```csharp
// Generated (automatic)
public readonly partial record struct UserId : IComparable<UserId>, IEquatable<UserId>
{
    // ... generated members
}

// Your extension with custom interfaces (manual)
public readonly partial record struct UserId : IFormattable, ISerializable
{
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        // Custom formatting implementation
    }
    
    // ISerializable implementation
}
```

## Error Messages

The generators now provide detailed error messages with context:

- **BG001**: General generator error with exception details
- **BG002**: File skipped with reason
- **BG003**: Duplicate generated file name detected
- **BG004**: Parsing error with file path and details
- **BG005**: Validation error with item name and context
- **BG006**: Missing required property with property name

Each error includes the file path, exception details (if applicable), and helpful context to identify and fix the issue.

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

## Migration Guide

If you're updating from a previous version with the hardcoded "CONFIDENTIAL" license header:

1. **No breaking changes** - Existing generated code continues to work
2. **License headers automatically updated** - Next build will use Apache 2.0 license
3. **Opt-in customization** - Use `GeneratorConfig` to customize if needed
4. **Interface compatibility** - Bravellian interfaces still included by default

To maintain the old license header (not recommended for public projects):

```csharp
GeneratorConfig.CustomLicenseHeader = @"
CONFIDENTIAL - Copyright (c) Bravellian LLC. All rights reserved.
See NOTICE.md for full restrictions and usage terms.
";
```

## Best Practices

1. **License Headers**: Set custom license headers in a project-wide initialization file
2. **Partial Classes**: Use partial classes to extend generated types without modifying generated code
3. **Interface Control**: Only disable Bravellian interfaces if you don't use Bravellian platform features
4. **Error Handling**: Review build output for generator warnings and errors with full context

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

### Type not implementing expected interfaces

Check the `GeneratorConfig.IncludeBravellianInterfaces` setting and ensure it matches your needs.

## Support

For issues or questions, please file an issue on the GitHub repository.
