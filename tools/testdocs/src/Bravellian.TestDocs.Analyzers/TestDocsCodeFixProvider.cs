using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bravellian.TestDocs.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TestDocsCodeFixProvider))]
public sealed class TestDocsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.MissingMetadataId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
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
        var indentation = await XmlDocHelper.GetIndentationAsync(document, method, cancellationToken).ConfigureAwait(false);
        var newline = await XmlDocHelper.GetNewLineAsync(document, cancellationToken).ConfigureAwait(false);
        var updatedMethod = XmlDocHelper.AddTemplate(method, indentation, newline);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        var newRoot = root.ReplaceNode(method, updatedMethod);
        return document.WithSyntaxRoot(newRoot);
    }
}
