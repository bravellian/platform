namespace Bravellian.TestDocs.Analyzers;

internal static class PlaceholderValues
{
    public static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "todo",
        "tbd",
        "fill me",
        "replace me",
        "add summary",
        "add intent",
        "add scenario",
        "add behavior",
    };
}
