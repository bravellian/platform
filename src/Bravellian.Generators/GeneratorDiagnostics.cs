namespace Bravellian.Generators;

using System;
using Microsoft.CodeAnalysis;

internal static class GeneratorDiagnostics
{
    private const string Category = "BravellianGenerators";

    private static readonly DiagnosticDescriptor ErrorDescriptor = new(
        id: "BG001",
        title: "Generator error",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SkippedDescriptor = new(
        id: "BG002",
        title: "Generator skipped file",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateHintNameDescriptor = new(
        id: "BGEN001",
        title: "Duplicate generated hint name",
        messageFormat: "Duplicate generated hint name '{0}'. Skipping duplicate to avoid AddSource collision.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static void ReportError(SourceProductionContext context, string message, Exception? exception = null)
    {
        var fullMessage = exception == null
            ? message
            : $"{message}\n{exception}";

        context.ReportDiagnostic(Diagnostic.Create(ErrorDescriptor, Location.None, fullMessage));
    }

    public static void ReportSkipped(SourceProductionContext context, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(SkippedDescriptor, Location.None, message));
    }

    public static void ReportDuplicateHintName(SourceProductionContext context, string hintName)
    {
        context.ReportDiagnostic(Diagnostic.Create(DuplicateHintNameDescriptor, Location.None, hintName));
    }
}
