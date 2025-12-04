// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.

#nullable enable

namespace Bravellian.Generators;

using System;

/// <summary>
/// Global configuration for code generation behavior.
/// Allows customization of license headers and interface generation.
/// </summary>
public static class GeneratorConfig
{
    private static string? _customLicenseHeader;
    private static bool _includeBravellianInterfaces = true;
    private static bool _generateAsPartialClasses = true;

    /// <summary>
    /// Gets or sets the custom license header to include after the // &lt;auto-generated/&gt; comment.
    /// If null or empty, the default Apache License header will be used.
    /// Multi-line headers should use \n for line breaks.
    /// </summary>
    public static string? CustomLicenseHeader
    {
        get => _customLicenseHeader;
        set => _customLicenseHeader = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to include Bravellian-specific interfaces
    /// (IHasValueConverter, IBgParsable, etc.) in generated types.
    /// Default is true for backward compatibility.
    /// When false, only standard .NET interfaces (IComparable, IEquatable, etc.) are included.
    /// </summary>
    public static bool IncludeBravellianInterfaces
    {
        get => _includeBravellianInterfaces;
        set => _includeBravellianInterfaces = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether generated types should be partial classes.
    /// Default is true, allowing users to extend generated types with additional members
    /// or implement additional interfaces in a separate partial class file.
    /// </summary>
    public static bool GenerateAsPartialClasses
    {
        get => _generateAsPartialClasses;
        set => _generateAsPartialClasses = value;
    }

    /// <summary>
    /// Gets the license header to use in generated code.
    /// Returns the custom header if set, otherwise returns the default Apache License header.
    /// </summary>
    internal static string GetLicenseHeader()
    {
        if (!string.IsNullOrWhiteSpace(_customLicenseHeader))
        {
            return FormatLicenseHeader(_customLicenseHeader);
        }

        return DefaultLicenseHeader;
    }

    /// <summary>
    /// Gets the default Apache License header used when no custom header is specified.
    /// </summary>
    private static string DefaultLicenseHeader => @"// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.";

    /// <summary>
    /// Formats a license header to ensure each line starts with //.
    /// Preserves empty lines for formatting purposes.
    /// </summary>
    private static string FormatLicenseHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var lines = header.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
        var formattedLines = new string[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            
            // Preserve empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                formattedLines[i] = "//";
            }
            else if (!line.StartsWith("//"))
            {
                formattedLines[i] = "// " + line;
            }
            else
            {
                formattedLines[i] = line;
            }
        }

        return string.Join("\n", formattedLines);
    }

    /// <summary>
    /// Gets the partial keyword if GenerateAsPartialClasses is true, otherwise empty string.
    /// </summary>
    internal static string GetPartialKeyword()
    {
        return _generateAsPartialClasses ? "partial " : "";
    }
}
