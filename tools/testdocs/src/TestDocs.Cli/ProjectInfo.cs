using System.Xml.Linq;

namespace TestDocs.Cli;

internal sealed class ProjectInfo
{
    public required string ProjectPath { get; init; }

    public required string ProjectName { get; init; }

    public required string ProjectDirectory { get; init; }

    public required string AssemblyName { get; init; }

    public required string RootNamespace { get; init; }

    public required IReadOnlyList<string> PackageReferences { get; init; }

    public static ProjectInfo Load(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var propertyGroups = document.Root?.Elements("PropertyGroup") ?? Enumerable.Empty<XElement>();
        var assemblyName = propertyGroups.Elements("AssemblyName").Select(element => element.Value).FirstOrDefault();
        var rootNamespace = propertyGroups.Elements("RootNamespace").Select(element => element.Value).FirstOrDefault();

        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        assemblyName ??= projectName;
        rootNamespace ??= assemblyName;

        var packageReferences = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectInfo
        {
            ProjectPath = projectPath,
            ProjectName = projectName,
            ProjectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty,
            AssemblyName = assemblyName,
            RootNamespace = rootNamespace,
            PackageReferences = packageReferences,
        };
    }
}
