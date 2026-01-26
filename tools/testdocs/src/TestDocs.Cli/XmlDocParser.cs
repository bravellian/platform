using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TestDocs.Cli;

internal static class XmlDocParser
{
    public static XDocument? TryParse(MethodDeclarationSyntax method)
    {
        var trivia = method.GetLeadingTrivia()
            .Select(item => item.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

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
}
