// CONFIDENTIAL - Copyright (c) Bravellian LLC. All rights reserved.
// See NOTICE.md for full restrictions and usage terms.

#nullable enable

namespace Bravellian.Generators;

/// <summary>
/// Global configuration for Entity Framework Core ValueConverter generation.
/// </summary>
public static class ValueConverterConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether returns true if ValueConverter generation is enabled (output path is set).
    /// </summary>
    public static bool IsEnabled { get; set; }
}
