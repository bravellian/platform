using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Bravellian.TestDocs.Analyzers;

internal static class XmlDocHelper
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static DocumentationCommentTriviaSyntax? GetDocumentationTrivia(MethodDeclarationSyntax method)
    {
        return method.GetLeadingTrivia()
            .Select(item => item.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
    }

    public static XDocument? TryParse(DocumentationCommentTriviaSyntax? trivia)
    {
        if (trivia == null)
        {
            return null;
        }

        var text = trivia.ToFullString();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                trimmed = trimmed[3..];
            }
            else if (trimmed.StartsWith("/**", StringComparison.Ordinal))
            {
                trimmed = trimmed[3..];
            }
            else if (trimmed.StartsWith("*/", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }
            else if (trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.StartsWith(" ", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            builder.AppendLine(trimmed);
        }

        var body = builder.ToString();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return XDocument.Parse($"<root>{body}</root>", LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return null;
        }
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return WhitespaceRegex.Replace(trimmed, " ");
    }

    public static async Task<string> GetIndentationAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(method.SpanStart);
        var indentationLength = GetIndentationLength(line);
        return indentationLength == 0 ? string.Empty : new string(' ', indentationLength);
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

    public static async Task<string> GetNewLineAsync(Document document, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (text.Lines.Count == 0)
        {
            return Environment.NewLine;
        }

        var line = text.Lines[0];
        var breakLength = line.SpanIncludingLineBreak.Length - line.Span.Length;
        if (breakLength <= 0)
        {
            return Environment.NewLine;
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
}
