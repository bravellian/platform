namespace TestDocs.Cli;

internal static class PathFilters
{
    public static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildDirectory(root, path))
            .Where(path => !IsUnderToolsTestDocs(root, path));
    }

    public static bool IsUnderBuildDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(segment, "artifacts", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUnderToolsTestDocs(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith("tools/testdocs/", StringComparison.OrdinalIgnoreCase);
    }
}
