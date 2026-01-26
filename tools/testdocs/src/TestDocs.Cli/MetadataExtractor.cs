using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TestDocs.Cli;

internal static class MetadataExtractor
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static TestMetadata Extract(XDocument? doc, string? namespaceName, string assemblyName, string className, string methodName)
    {
        var metadata = new TestMetadata
        {
            Tags = Array.Empty<string>(),
        };

        if (doc?.Root == null)
        {
            return metadata;
        }

        metadata.Summary = GetElementValue(doc, "summary", false);
        metadata.Intent = GetElementValue(doc, "intent", false);
        metadata.Scenario = GetElementValue(doc, "scenario", false);
        metadata.Behavior = GetElementValue(doc, "behavior", false);
        metadata.FailureSignal = GetElementValue(doc, "failuresignal", false);
        metadata.Risk = GetElementValue(doc, "risk", false);
        metadata.Notes = GetElementValue(doc, "notes", true);
        metadata.Category = GetElementValue(doc, "category", false);
        metadata.TestId = GetElementValue(doc, "testid", false);

        var tagsValue = GetElementValue(doc, "tags", false);
        if (!string.IsNullOrWhiteSpace(tagsValue))
        {
            metadata.Tags = tagsValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray();
        }

        var originElement = doc.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "origin");
        if (originElement != null)
        {
            metadata.Origin = new OriginInfo
            {
                Kind = Normalize(originElement.Attribute("kind")?.Value, false),
                Id = Normalize(originElement.Attribute("id")?.Value, false),
                Date = Normalize(originElement.Attribute("date")?.Value, false),
                Text = Normalize(originElement.Value, false),
            };
        }

        if (string.IsNullOrWhiteSpace(metadata.TestId))
        {
            var member = string.IsNullOrWhiteSpace(namespaceName)
                ? $"{className}.{methodName}"
                : $"{namespaceName}.{className}.{methodName}";
            metadata.TestId = $"{assemblyName}:{member}";
        }

        if (string.IsNullOrWhiteSpace(metadata.Category))
        {
            metadata.Category = DeriveCategory(namespaceName);
        }

        return metadata;
    }

    public static string DeriveCategory(string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return "General";
        }

        var marker = ".Tests.";
        var index = namespaceName.IndexOf(marker, StringComparison.Ordinal);
        if (index >= 0)
        {
            var after = namespaceName[(index + marker.Length)..];
            var segment = after.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(segment))
            {
                return segment;
            }
        }

        var parts = namespaceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var take = parts.Length >= 3 ? 3 : parts.Length;
        if (take == 0)
        {
            return "General";
        }

        return string.Join('.', parts.Take(take));
    }

    public static IReadOnlyList<string> MissingRequired(TestMetadata metadata)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(metadata.Summary))
        {
            missing.Add("summary");
        }

        if (string.IsNullOrWhiteSpace(metadata.Intent))
        {
            missing.Add("intent");
        }

        if (string.IsNullOrWhiteSpace(metadata.Scenario))
        {
            missing.Add("scenario");
        }

        if (string.IsNullOrWhiteSpace(metadata.Behavior))
        {
            missing.Add("behavior");
        }

        return missing;
    }

    public static TestStatus DetermineStatus(TestMetadata metadata, IReadOnlyList<string> missingRequired)
    {
        if (missingRequired.Count > 0)
        {
            return TestStatus.MissingRequired;
        }

        if (metadata.Origin != null && string.IsNullOrWhiteSpace(metadata.Origin.Kind))
        {
            return TestStatus.InvalidFormat;
        }

        return TestStatus.Compliant;
    }

    private static string? GetElementValue(XDocument doc, string elementName, bool preserveWhitespace)
    {
        var element = doc.Root?.Elements().FirstOrDefault(node => node.Name.LocalName == elementName);
        if (element == null)
        {
            return null;
        }

        return Normalize(element.Value, preserveWhitespace);
    }

    private static string? Normalize(string? value, bool preserveWhitespace)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (preserveWhitespace)
        {
            return trimmed.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        return WhitespaceRegex.Replace(trimmed, " ");
    }
}
