using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TestDocs.Cli;

internal static class TestDiscovery
{
    public static List<TestRecord> FindTests(ProjectInfo project, string repoRoot)
    {
        var tests = new List<TestRecord>();

        foreach (var filePath in PathFilters.EnumerateSourceFiles(project.ProjectDirectory))
        {
            var sourceText = File.ReadAllText(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(), filePath, Encoding.UTF8);
            var root = syntaxTree.GetCompilationUnitRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!HasTestMethodAttribute(method))
                {
                    continue;
                }

                var namespaceName = GetNamespace(method);
                var className = GetContainingTypeName(method);
                var member = string.IsNullOrWhiteSpace(namespaceName)
                    ? $"{className}.{method.Identifier.Text}"
                    : $"{namespaceName}.{className}.{method.Identifier.Text}";

                var xmlDoc = XmlDocParser.TryParse(method);
                var metadata = MetadataExtractor.Extract(xmlDoc, namespaceName, project.AssemblyName, className, method.Identifier.Text);

                var lineSpan = syntaxTree.GetLineSpan(method.Identifier.Span);
                var lineNumber = lineSpan.StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');

                var missingRequired = MetadataExtractor.MissingRequired(metadata);
                var status = MetadataExtractor.DetermineStatus(metadata, missingRequired);

                tests.Add(new TestRecord
                {
                    TestId = metadata.TestId ?? $"{project.AssemblyName}:{member}",
                    Category = metadata.Category ?? MetadataExtractor.DeriveCategory(namespaceName),
                    Tags = metadata.Tags,
                    Origin = metadata.Origin,
                    Summary = metadata.Summary,
                    Intent = metadata.Intent,
                    Scenario = metadata.Scenario,
                    Behavior = metadata.Behavior,
                    FailureSignal = metadata.FailureSignal,
                    Risk = metadata.Risk,
                    Notes = metadata.Notes,
                    Source = new SourceInfo
                    {
                        File = relativePath,
                        Line = lineNumber,
                        Member = member,
                    },
                    Status = status,
                    Project = project.ProjectName,
                    MissingRequired = missingRequired,
                });
            }
        }

        return tests;
    }

    private static bool HasTestMethodAttribute(MethodDeclarationSyntax method)
    {
        return HasAttribute(method.AttributeLists, "TestMethod") ||
               HasAttribute(method.AttributeLists, "DataTestMethod") ||
               HasAttribute(method.AttributeLists, "Fact") ||
               HasAttribute(method.AttributeLists, "Theory") ||
               HasAttribute(method.AttributeLists, "Test") ||
               HasAttribute(method.AttributeLists, "TestCase") ||
               HasAttribute(method.AttributeLists, "TestCaseSource");
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var name = AttributeName(attribute.Name);
                if (string.Equals(name, attributeName, StringComparison.Ordinal))
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

    private static string? GetNamespace(SyntaxNode node)
    {
        var namespaceNode = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceNode?.Name.ToString();
    }

    private static string GetContainingTypeName(SyntaxNode node)
    {
        var names = node.Ancestors().OfType<TypeDeclarationSyntax>()
            .Select(type => type.Identifier.Text)
            .Reverse()
            .ToList();

        return names.Count == 0 ? "UnknownType" : string.Join(".", names);
    }
}
