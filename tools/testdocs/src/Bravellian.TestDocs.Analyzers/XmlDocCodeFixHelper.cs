using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Bravellian.TestDocs.Analyzers;

internal static class XmlDocCodeFixHelper
{
    public static async Task<string> GetIndentationAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(method.SpanStart);
        var indentationLength = GetIndentationLength(line);
        return indentationLength == 0 ? string.Empty : new string(' ', indentationLength);
    }

    public static async Task<string> GetNewLineAsync(Document document, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (text.Lines.Count == 0)
        {
            return "\n";
        }

        var line = text.Lines[0];
        var breakLength = line.SpanIncludingLineBreak.Length - line.Span.Length;
        if (breakLength <= 0)
        {
            return "\n";
        }

        return text.ToString(new TextSpan(line.End, breakLength));
    }

    public static MethodDeclarationSyntax AddTemplate(MethodDeclarationSyntax method, string indentation, string newline)
    {
        var lines = new[]
        {
            $"{indentation}/// <summary>TODO</summary>",
            $"{indentation}/// <intent>TODO</intent>",
            $"{indentation}/// <scenario>TODO</scenario>",
            $"{indentation}/// <behavior>TODO</behavior>",
        };

        var docComment = string.Join(newline, lines) + newline;
        var trivia = SyntaxFactory.ParseLeadingTrivia(docComment);
        return method.WithLeadingTrivia(trivia.AddRange(method.GetLeadingTrivia()));
    }

    private static int GetIndentationLength(TextLine line)
    {
        var lineText = line.ToString();
        var offset = 0;
        while (offset < lineText.Length && char.IsWhiteSpace(lineText[offset]))
        {
            offset++;
        }

        return offset;
    }
}
