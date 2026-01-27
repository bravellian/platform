using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bravellian.TestDocs.Analyzers;

/// <summary>
/// Code fixes for missing test documentation metadata.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TestDocsCodeFixProvider))]
public sealed class TestDocsCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the diagnostic IDs this provider can fix.
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.MissingMetadataId);

    /// <summary>
    /// Gets the fix-all provider.
    /// </summary>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// Registers code fixes.
    /// </summary>
    /// <param name="context">Code fix context.</param>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (context.Document is null)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.FirstOrDefault();
        if (diagnostic == null)
        {
            return;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null)
        {
            return;
        }

        var existingTrivia = XmlDocHelper.GetDocumentationTrivia(method);
        if (existingTrivia != null)
        {
            return;
        }

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                "Add test documentation template",
                cancellationToken => AddTemplateAsync(context.Document, method, cancellationToken),
                equivalenceKey: "AddTestDocTemplate"),
            diagnostic);
    }

    private static async Task<Document> AddTemplateAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
    {
        var indentation = await XmlDocCodeFixHelper.GetIndentationAsync(document, method, cancellationToken).ConfigureAwait(false);
        var newline = await XmlDocCodeFixHelper.GetNewLineAsync(document, cancellationToken).ConfigureAwait(false);
        var updatedMethod = XmlDocCodeFixHelper.AddTemplate(method, indentation, newline);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        var newRoot = root.ReplaceNode(method, updatedMethod);
        return document.WithSyntaxRoot(newRoot);
    }
}
