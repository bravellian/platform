using System.Text;

namespace TestDocs.Cli;

internal static class MarkdownWriter
{
    public static void WriteAll(StatsReport report, string outDir)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "by-category"));
        Directory.CreateDirectory(Path.Combine(outDir, "by-tag"));

        var readme = BuildReadme(report);
        File.WriteAllText(Path.Combine(outDir, "README.md"), readme);
        File.WriteAllText(Path.Combine(outDir, "index.md"), readme);
        File.WriteAllText(Path.Combine(outDir, "stats.md"), BuildStats(report));
        File.WriteAllText(Path.Combine(outDir, "missing-metadata.md"), BuildMissingMetadata(report));
        File.WriteAllText(Path.Combine(outDir, "regressions.md"), BuildRegressions(report));

        WriteCategoryPages(report, outDir);
        WriteTagPages(report, outDir);
        File.WriteAllText(Path.Combine(outDir, "by-category", "index.md"), BuildCategoryIndex(report));
        File.WriteAllText(Path.Combine(outDir, "by-tag", "index.md"), BuildTagIndex(report));
    }

    private static string BuildReadme(StatsReport report)
    {
        var compliance = report.Summary.Total == 0
            ? 1.0
            : (double)report.Summary.Compliant / report.Summary.Total;

        var topCategories = report.ByCategory
            .OrderByDescending(category => category.Total)
            .ThenBy(category => category.Category, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# Test Documentation");
        builder.AppendLine();
        AppendInvariant(builder, $"- Total tests: {report.Summary.Total}");
        AppendInvariant(builder, $"- Compliant: {report.Summary.Compliant} ({FormatHelpers.FormatPercent(compliance)})");
        AppendInvariant(builder, $"- Missing required: {report.Summary.MissingRequired}");
        AppendInvariant(builder, $"- Invalid format: {report.Summary.InvalidFormat}");
        builder.AppendLine();
        builder.AppendLine("## Top categories");
        if (topCategories.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No tests discovered.");
        }
        else
        {
            builder.AppendLine();
            foreach (var category in topCategories)
            {
                AppendInvariant(builder, $"- {category.Category} ({category.Total})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Browse");
        builder.AppendLine();
        builder.AppendLine("- [By category](by-category)");
        builder.AppendLine("- [By tag](by-tag)");
        builder.AppendLine("- [Missing metadata](missing-metadata.md)");
        builder.AppendLine("- [Regressions](regressions.md)");
        builder.AppendLine("- [Stats](stats.md)");

        return builder.ToString();
    }

    private static void WriteCategoryPages(StatsReport report, string outDir)
    {
        foreach (var category in report.ByCategory)
        {
            var slug = Slugify(category.Category);
            var path = Path.Combine(outDir, "by-category", $"{slug}.md");
            var tests = report.Tests.Where(test => test.Category == category.Category)
                .OrderBy(test => test.TestId, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            AppendInvariant(builder, $"# {category.Category}");
            builder.AppendLine();
            AppendInvariant(builder, $"Total tests: {category.Total}");
            builder.AppendLine();
            foreach (var test in tests)
            {
                var summary = test.Summary ?? "(missing summary)";
                var intent = test.Intent ?? "(missing intent)";
                AppendInvariant(builder, $"- **{test.TestId}**");
                AppendInvariant(builder, $"  - Summary: {summary}");
                AppendInvariant(builder, $"  - Intent: {intent}");
                AppendInvariant(builder, $"  - Tags: {FormatTags(test.Tags)}");
                AppendInvariant(builder, $"  - Source: [{test.Source.File}#L{test.Source.Line}]({test.Source.File}#L{test.Source.Line})");
                if (test.Origin != null)
                {
                    AppendInvariant(builder, $"  - Origin: {FormatOrigin(test.Origin)}");
                }
            }

            File.WriteAllText(path, builder.ToString());
        }
    }

    private static void WriteTagPages(StatsReport report, string outDir)
    {
        foreach (var tag in report.ByTag)
        {
            var slug = Slugify(tag.Tag);
            var path = Path.Combine(outDir, "by-tag", $"{slug}.md");
            var tests = report.Tests
                .Where(test => test.Tags.Contains(tag.Tag, StringComparer.Ordinal))
                .OrderBy(test => test.TestId, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            AppendInvariant(builder, $"# {tag.Tag}");
            builder.AppendLine();
            AppendInvariant(builder, $"Total tests: {tag.Total}");
            builder.AppendLine();
            foreach (var test in tests)
            {
                var summary = test.Summary ?? "(missing summary)";
                AppendInvariant(builder, $"- **{test.TestId}**");
                AppendInvariant(builder, $"  - Summary: {summary}");
                AppendInvariant(builder, $"  - Category: {test.Category}");
                AppendInvariant(builder, $"  - Source: [{test.Source.File}#L{test.Source.Line}]({test.Source.File}#L{test.Source.Line})");
            }

            File.WriteAllText(path, builder.ToString());
        }
    }

    private static string BuildMissingMetadata(StatsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Missing required metadata");
        builder.AppendLine();

        var missing = report.Tests
            .Where(test => test.Status == TestStatus.MissingRequired)
            .OrderBy(test => test.Category, StringComparer.Ordinal)
            .ThenBy(test => test.TestId, StringComparer.Ordinal)
            .ToList();

        if (missing.Count == 0)
        {
            builder.AppendLine("All tests include required metadata.");
            return builder.ToString();
        }

        foreach (var test in missing)
        {
            AppendInvariant(builder, $"- **{test.TestId}**");
            AppendInvariant(builder, $"  - Missing: {string.Join(", ", test.MissingRequired)}");
            AppendInvariant(builder, $"  - Source: [{test.Source.File}#L{test.Source.Line}]({test.Source.File}#L{test.Source.Line})");
        }

        return builder.ToString();
    }

    private static string BuildRegressions(StatsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Regression coverage");
        builder.AppendLine();

        var regressions = report.Tests
            .Where(test => test.Origin != null)
            .OrderBy(test => test.Category, StringComparer.Ordinal)
            .ThenBy(test => test.TestId, StringComparer.Ordinal)
            .ToList();

        if (regressions.Count == 0)
        {
            builder.AppendLine("No regression-origin tests recorded.");
            return builder.ToString();
        }

        foreach (var test in regressions)
        {
            AppendInvariant(builder, $"- **{test.TestId}**");
            AppendInvariant(builder, $"  - Origin: {FormatOrigin(test.Origin!)}");
            AppendInvariant(builder, $"  - Source: [{test.Source.File}#L{test.Source.Line}]({test.Source.File}#L{test.Source.Line})");
        }

        return builder.ToString();
    }

    private static string BuildStats(StatsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test documentation stats");
        builder.AppendLine();
        AppendInvariant(builder, $"Generated at (UTC): {report.GeneratedAtUtc:O}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Metric | Count |");
        builder.AppendLine("| --- | ---: |");
        AppendInvariant(builder, $"| Total | {report.Summary.Total} |");
        AppendInvariant(builder, $"| Compliant | {report.Summary.Compliant} |");
        AppendInvariant(builder, $"| Missing required | {report.Summary.MissingRequired} |");
        AppendInvariant(builder, $"| Invalid format | {report.Summary.InvalidFormat} |");

        builder.AppendLine();
        builder.AppendLine("## By category");
        builder.AppendLine();
        builder.AppendLine("| Category | Total | Compliant |");
        builder.AppendLine("| --- | ---: | ---: |");
        foreach (var category in report.ByCategory)
        {
            AppendInvariant(builder, $"| {category.Category} | {category.Total} | {category.Compliant} |");
        }

        builder.AppendLine();
        builder.AppendLine("## By tag");
        builder.AppendLine();
        builder.AppendLine("| Tag | Total |");
        builder.AppendLine("| --- | ---: |");
        foreach (var tag in report.ByTag)
        {
            AppendInvariant(builder, $"| {tag.Tag} | {tag.Total} |");
        }

        builder.AppendLine();
        builder.AppendLine("## By project");
        builder.AppendLine();
        builder.AppendLine("| Project | Total | Compliant |");
        builder.AppendLine("| --- | ---: | ---: |");
        foreach (var project in report.ByProject)
        {
            AppendInvariant(builder, $"| {project.Project} | {project.Total} | {project.Compliant} |");
        }

        return builder.ToString();
    }

    private static string BuildCategoryIndex(StatsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Categories");
        builder.AppendLine();

        if (report.ByCategory.Count == 0)
        {
            builder.AppendLine("No categories recorded.");
            return builder.ToString();
        }

        foreach (var category in report.ByCategory)
        {
            var slug = Slugify(category.Category);
            AppendInvariant(builder, $"- [{category.Category}]({slug}.md)");
        }

        return builder.ToString();
    }

    private static string BuildTagIndex(StatsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tags");
        builder.AppendLine();

        if (report.ByTag.Count == 0)
        {
            builder.AppendLine("No tags recorded.");
            return builder.ToString();
        }

        foreach (var tag in report.ByTag)
        {
            var slug = Slugify(tag.Tag);
            AppendInvariant(builder, $"- [{tag.Tag}]({slug}.md)");
        }

        return builder.ToString();
    }

    private static string FormatTags(IReadOnlyCollection<string> tags)
    {
        return tags.Count == 0 ? "(none)" : string.Join(", ", tags);
    }

    private static string FormatOrigin(OriginInfo origin)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(origin.Kind))
        {
            parts.Add(origin.Kind);
        }
        if (!string.IsNullOrWhiteSpace(origin.Id))
        {
            parts.Add(origin.Id);
        }
        if (!string.IsNullOrWhiteSpace(origin.Date))
        {
            parts.Add(origin.Date);
        }
        var header = parts.Count == 0 ? "origin" : string.Join(" | ", parts);
        if (string.IsNullOrWhiteSpace(origin.Text))
        {
            return header;
        }

        return $"{header} - {origin.Text}";
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch == '.' || ch == ' ' || ch == '_' || ch == '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "uncategorized" : slug;
    }

    private static void AppendInvariant(StringBuilder builder, FormattableString value)
    {
        builder.AppendLine(FormattableString.Invariant(value));
    }
}
