using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bravellian.TestDocs.Analyzers;

internal static class TestAttributeHelper
{
    private static readonly string[] TestAttributes =
    {
        "TestMethod",
        "DataTestMethod",
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
    };

    public static bool IsTestMethod(MethodDeclarationSyntax method)
    {
        return HasAnyAttribute(method.AttributeLists, TestAttributes);
    }

    private static bool HasAnyAttribute(SyntaxList<AttributeListSyntax> attributeLists, IEnumerable<string> names)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var name = AttributeName(attribute.Name);
                if (names.Any(expected => string.Equals(name, expected, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string AttributeName(NameSyntax name)
    {
        string text = name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => name.ToString(),
        };

        return text.EndsWith("Attribute", StringComparison.Ordinal) ? text[..^9] : text;
    }
}
