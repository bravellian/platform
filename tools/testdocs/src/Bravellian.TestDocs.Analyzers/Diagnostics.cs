using Microsoft.CodeAnalysis;

namespace Bravellian.TestDocs.Analyzers;

internal static class Diagnostics
{
    public const string MissingMetadataId = "TD001";
    public const string PlaceholderMetadataId = "TD002";

    public static readonly DiagnosticDescriptor MissingMetadata = new(
        MissingMetadataId,
        "Missing required test documentation",
        "Test documentation is missing required tags: {0}",
        "TestDocs",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PlaceholderMetadata = new(
        PlaceholderMetadataId,
        "Placeholder test documentation",
        "Test documentation contains placeholder or empty values for: {0}",
        "TestDocs",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
