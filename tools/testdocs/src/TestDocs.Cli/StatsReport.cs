namespace TestDocs.Cli;

internal sealed class StatsReport
{
    public DateTime GeneratedAtUtc { get; set; }

    public RepoInfo Repo { get; set; } = new();

    public SummaryStats Summary { get; set; } = new();

    public List<CategoryStats> ByCategory { get; set; } = new();

    public List<TagStats> ByTag { get; set; } = new();

    public List<ProjectStats> ByProject { get; set; } = new();

    public List<TestRecord> Tests { get; set; } = new();
}
