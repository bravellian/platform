namespace Bravellian.Generators;

using System;
using System.Text;
using Microsoft.CodeAnalysis;

internal static class GeneratorDiagnostics
{
    private const string Category = "BravellianGenerators";
    private const string HelpLinkUri = "https://github.com/bravellian/platform/blob/main/docs/generators.md";

    private static readonly DiagnosticDescriptor ErrorDescriptor = new(
        id: "BG001",
        title: "Generator error",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkUri);

    private static readonly DiagnosticDescriptor SkippedDescriptor = new(
        id: "BG002",
        title: "Generator skipped file",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkUri);

    private static readonly DiagnosticDescriptor DuplicateHintNameDescriptor = new(
        id: "BG003",
        title: "Duplicate generated hint name",
        messageFormat: "Duplicate generated hint name '{0}'. Skipping duplicate to avoid AddSource collision.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkUri);

    private static readonly DiagnosticDescriptor ValidationErrorDescriptor = new(
        id: "BG004",
        title: "Validation error",
        messageFormat: "Validation failed for '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkUri);

    private static readonly DiagnosticDescriptor MissingRequiredPropertyDescriptor = new(
        id: "BG005",
        title: "Missing required property",
        messageFormat: "Required property '{0}' is missing in file '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkUri);

    public static void ReportError(SourceProductionContext context, string message, Exception? exception = null, string? filePath = null)
    {
        var messageBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(filePath))
        {
            messageBuilder.AppendLine($"File: {filePath}");
        }

        messageBuilder.Append(message);

        if (exception != null)
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("Exception details:");
            messageBuilder.AppendLine($"  Type: {exception.GetType().Name}");
            messageBuilder.AppendLine($"  Message: {exception.Message}");

            if (exception.InnerException != null)
            {
                messageBuilder.AppendLine($"  Inner Exception: {exception.InnerException.Message}");
            }

            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                messageBuilder.AppendLine($"  Stack Trace: {exception.StackTrace}");
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(ErrorDescriptor, Location.None, messageBuilder.ToString()));
    }

    public static void ReportSkipped(SourceProductionContext context, string message, string? filePath = null)
    {
        var fullMessage = string.IsNullOrEmpty(filePath)
            ? message
            : $"{message} (File: {filePath})";

        context.ReportDiagnostic(Diagnostic.Create(SkippedDescriptor, Location.None, fullMessage));
    }

    public static void ReportDuplicateHintName(SourceProductionContext context, string hintName)
    {
        context.ReportDiagnostic(Diagnostic.Create(DuplicateHintNameDescriptor, Location.None, hintName));
    }

    public static void ReportValidationError(SourceProductionContext context, string itemName, string errorMessage, string? filePath = null)
    {
        var fullMessage = string.IsNullOrEmpty(filePath)
            ? $"{errorMessage}"
            : $"{errorMessage} (File: {filePath})";

        context.ReportDiagnostic(Diagnostic.Create(ValidationErrorDescriptor, Location.None, itemName, fullMessage));
    }

    public static void ReportMissingRequiredProperty(SourceProductionContext context, string propertyName, string filePath)
    {
        context.ReportDiagnostic(Diagnostic.Create(MissingRequiredPropertyDescriptor, Location.None, propertyName, filePath));
    }
}
