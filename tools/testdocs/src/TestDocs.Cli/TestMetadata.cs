namespace TestDocs.Cli;

internal sealed class TestMetadata
{
    public string? Summary { get; set; }

    public string? Intent { get; set; }

    public string? Scenario { get; set; }

    public string? Behavior { get; set; }

    public string? FailureSignal { get; set; }

    public OriginInfo? Origin { get; set; }

    public string? Risk { get; set; }

    public string? Notes { get; set; }

    public string[] Tags { get; set; } = Array.Empty<string>();

    public string? Category { get; set; }

    public string? TestId { get; set; }
}
