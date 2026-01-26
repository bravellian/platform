namespace TestDocs.Cli;

internal static class ProjectDiscovery
{
    public static List<ProjectInfo> FindTestProjects(string repoRoot)
    {
        var projects = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !PathFilters.IsUnderBuildDirectory(repoRoot, path))
            .Where(path => !PathFilters.IsUnderToolsTestDocs(repoRoot, path))
            .Select(ProjectInfo.Load)
            .ToList();

        var testProjects = new List<ProjectInfo>();
        foreach (var project in projects)
        {
            if (IsKnownTestProject(project) || ContainsTestAttributes(project.ProjectDirectory))
            {
                testProjects.Add(project);
            }
        }

        return testProjects;
    }

    private static bool IsKnownTestProject(ProjectInfo project)
    {
        if (project.PackageReferences.Any(reference => string.Equals(reference, "xunit", StringComparison.OrdinalIgnoreCase)) ||
            project.PackageReferences.Any(reference => string.Equals(reference, "xunit.v3", StringComparison.OrdinalIgnoreCase)) ||
            project.PackageReferences.Any(reference => string.Equals(reference, "xunit.core", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (project.PackageReferences.Any(reference => string.Equals(reference, "NUnit", StringComparison.OrdinalIgnoreCase)) ||
            project.PackageReferences.Any(reference => string.Equals(reference, "NUnit3TestAdapter", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (project.PackageReferences.Any(reference => string.Equals(reference, "MSTest.TestFramework", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (project.PackageReferences.Any(reference => string.Equals(reference, "MSTest.TestAdapter", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return project.PackageReferences.Any(reference => string.Equals(reference, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsTestAttributes(string projectDirectory)
    {
        var markers = new[]
        {
            "[TestClass",
            "[TestMethod",
            "[DataTestMethod",
            "[Fact",
            "[Theory",
            "[Test",
            "[TestCase",
            "[TestCaseSource",
            "[TestFixture",
        };

        foreach (var file in PathFilters.EnumerateSourceFiles(projectDirectory))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (markers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
