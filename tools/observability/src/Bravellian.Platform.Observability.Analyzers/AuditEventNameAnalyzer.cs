using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bravellian.Platform.Observability.Analyzers;

/// <summary>
/// Validates audit event naming conventions for <c>AuditEvent</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuditEventNameAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Gets the diagnostics supported by this analyzer.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.AuditEventNameFormat);

    /// <summary>
    /// Initializes the analyzer.
    /// </summary>
    /// <param name="context">Analysis context.</param>
    public override void Initialize(AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var auditEventType = startContext.Compilation.GetTypeByMetadataName(
                "Bravellian.Platform.Audit.AuditEvent");
            if (auditEventType is null)
            {
                return;
            }

            startContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeObjectCreation(nodeContext, auditEventType),
                SyntaxKind.ObjectCreationExpression);
        });
    }

    private static void AnalyzeObjectCreation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol auditEventType)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        var createdType = context.SemanticModel.GetTypeInfo(creation).Type;
        if (!SymbolEqualityComparer.Default.Equals(createdType, auditEventType))
        {
            return;
        }

        if (creation.ArgumentList is null)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor)
        {
            return;
        }

        var nameParameter = constructor.Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "name", StringComparison.Ordinal));
        if (nameParameter is null)
        {
            return;
        }

        var nameArgument = FindArgument(creation.ArgumentList, constructor, nameParameter);
        if (nameArgument is null)
        {
            return;
        }

        var constantValue = context.SemanticModel.GetConstantValue(nameArgument.Expression);
        if (!constantValue.HasValue || constantValue.Value is not string nameValue)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nameValue))
        {
            return;
        }

        if (IsValidName(nameValue))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.AuditEventNameFormat,
            nameArgument.Expression.GetLocation(),
            nameValue));
    }

    private static ArgumentSyntax? FindArgument(
        ArgumentListSyntax arguments,
        IMethodSymbol constructor,
        IParameterSymbol nameParameter)
    {
        foreach (var argument in arguments.Arguments)
        {
            if (argument.NameColon is null)
            {
                continue;
            }

            var name = argument.NameColon.Name.Identifier.ValueText;
            if (string.Equals(name, nameParameter.Name, StringComparison.Ordinal))
            {
                return argument;
            }
        }

        var index = constructor.Parameters.IndexOf(nameParameter);
        if (index >= 0 && index < arguments.Arguments.Count)
        {
            return arguments.Arguments[index];
        }

        return null;
    }

    private static bool IsValidName(string name)
    {
        var segmentLength = 0;
        foreach (var character in name)
        {
            if (character == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }

                segmentLength = 0;
                continue;
            }

            if (!IsLowerAlphaNumeric(character))
            {
                return false;
            }

            segmentLength++;
        }

        return segmentLength > 0;
    }

    private static bool IsLowerAlphaNumeric(char character)
    {
        return (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');
    }
}
