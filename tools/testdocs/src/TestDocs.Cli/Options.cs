namespace TestDocs.Cli;

internal sealed class Options
{
    public required string RepoRoot { get; init; }

    public required string OutDir { get; init; }

    public bool Strict { get; init; }

    public double MinCompliance { get; init; }

    public OutputFormat Format { get; init; }
}
