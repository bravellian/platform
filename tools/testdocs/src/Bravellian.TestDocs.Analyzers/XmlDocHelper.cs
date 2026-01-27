using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bravellian.TestDocs.Analyzers;

internal static class XmlDocHelper
{
    private static readonly string[] LineSeparators = { "\r\n", "\n" };
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
        var lines = text.Split(LineSeparators, StringSplitOptions.None);
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
            else if (trimmed.StartsWith('*'))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.StartsWith(' '))
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
        catch (XmlException)
        {
            return null;
        }
        catch (ArgumentException)
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

}
