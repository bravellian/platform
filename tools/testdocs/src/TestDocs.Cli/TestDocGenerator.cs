using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestDocs.Cli;

internal sealed class TestDocGenerator
{
    private readonly Options options;

    public TestDocGenerator(Options options)
    {
        this.options = options;
    }

    public GenerationResult Generate()
    {
        var projects = ProjectDiscovery.FindTestProjects(options.RepoRoot);
        var tests = new List<TestRecord>();

        foreach (var project in projects)
        {
            var projectTests = TestDiscovery.FindTests(project, options.RepoRoot);
            tests.AddRange(projectTests);
        }

        tests = tests
            .OrderBy(test => test.Category, StringComparer.Ordinal)
            .ThenBy(test => test.TestId, StringComparer.Ordinal)
            .ToList();

        var summary = BuildSummary(tests);
        var byCategory = BuildByCategory(tests);
        var byTag = BuildByTag(tests);
        var byProject = BuildByProject(tests);

        var report = new StatsReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Repo = new RepoInfo { DefaultBranch = "main" },
            Summary = summary,
            ByCategory = byCategory,
            ByTag = byTag,
            ByProject = byProject,
            Tests = tests,
        };

        var compliance = summary.Total == 0 ? 1.0 : (double)summary.Compliant / summary.Total;
        var result = new GenerationResult(report, compliance);

        var outDir = options.OutDir;
        Directory.CreateDirectory(outDir);
        if (options.Format is OutputFormat.Json or OutputFormat.Both)
        {
            WriteJson(report, Path.Combine(outDir, "stats.json"));
        }

        if (options.Format is OutputFormat.Markdown or OutputFormat.Both)
        {
            MarkdownWriter.WriteAll(report, outDir);
        }

        return result;
    }

    private static SummaryStats BuildSummary(IReadOnlyCollection<TestRecord> tests)
    {
        var compliant = tests.Count(test => test.Status == TestStatus.Compliant);
        var missing = tests.Count(test => test.Status == TestStatus.MissingRequired);
        var invalid = tests.Count(test => test.Status == TestStatus.InvalidFormat);

        return new SummaryStats
        {
            Total = tests.Count,
            Compliant = compliant,
            MissingRequired = missing,
            InvalidFormat = invalid,
        };
    }

    private static List<CategoryStats> BuildByCategory(IReadOnlyCollection<TestRecord> tests)
    {
        return tests
            .GroupBy(test => test.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CategoryStats
            {
                Category = group.Key,
                Total = group.Count(),
                Compliant = group.Count(test => test.Status == TestStatus.Compliant),
            })
            .ToList();
    }

    private static List<TagStats> BuildByTag(IReadOnlyCollection<TestRecord> tests)
    {
        return tests
            .SelectMany(test => test.Tags.Select(tag => (Tag: tag, Test: test)))
            .GroupBy(entry => entry.Tag, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new TagStats
            {
                Tag = group.Key,
                Total = group.Select(entry => entry.Test).Distinct().Count(),
            })
            .ToList();
    }

    private static List<ProjectStats> BuildByProject(IReadOnlyCollection<TestRecord> tests)
    {
        return tests
            .GroupBy(test => test.Project, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ProjectStats
            {
                Project = group.Key,
                Total = group.Count(),
                Compliant = group.Count(test => test.Status == TestStatus.Compliant),
            })
            .ToList();
    }

    private static void WriteJson(StatsReport report, string path)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        jsonOptions.Converters.Add(new TestStatusJsonConverter());

        var json = JsonSerializer.Serialize(report, jsonOptions);
        File.WriteAllText(path, json + "\n");
    }
}
