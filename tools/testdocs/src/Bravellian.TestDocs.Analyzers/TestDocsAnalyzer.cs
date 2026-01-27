using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Xml.Linq;

namespace Bravellian.TestDocs.Analyzers;

/// <summary>
/// Analyzer that enforces required XML documentation tags on test methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestDocsAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] RequiredTags = { "summary", "intent", "scenario", "behavior" };

    /// <summary>
    /// Gets the diagnostics supported by this analyzer.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MissingMetadata, Diagnostics.PlaceholderMetadata);

    /// <summary>
    /// Initializes the analyzer.
    /// </summary>
    /// <param name="context">Analysis context.</param>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax method)
        {
            return;
        }

        if (!TestAttributeHelper.IsTestMethod(method))
        {
            return;
        }

        var trivia = XmlDocHelper.GetDocumentationTrivia(method);
        var document = XmlDocHelper.TryParse(trivia);

        var missingTags = new List<string>();
        var placeholderTags = new List<string>();

        foreach (var tag in RequiredTags)
        {
            var value = GetElementValue(document, tag);
            if (value == null)
            {
                missingTags.Add(tag);
                continue;
            }

            if (IsPlaceholder(value))
            {
                placeholderTags.Add(tag);
            }
        }

        if (missingTags.Count > 0)
        {
            var message = string.Join(", ", missingTags.OrderBy(tag => tag, StringComparer.Ordinal));
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.MissingMetadata, method.Identifier.GetLocation(), message));
        }

        if (placeholderTags.Count > 0)
        {
            var message = string.Join(", ", placeholderTags.OrderBy(tag => tag, StringComparer.Ordinal));
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.PlaceholderMetadata, method.Identifier.GetLocation(), message));
        }
    }

    private static string? GetElementValue(XDocument? document, string tag)
    {
        if (document?.Root == null)
        {
            return null;
        }

        var element = document.Root.Elements().FirstOrDefault(node => string.Equals(node.Name.LocalName, tag, StringComparison.OrdinalIgnoreCase));
        if (element == null)
        {
            return null;
        }

        var normalized = XmlDocHelper.Normalize(element.Value);
        return normalized;
    }

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = XmlDocHelper.Normalize(value) ?? string.Empty;
        return PlaceholderValues.Values.Contains(normalized);
    }
}
