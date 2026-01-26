using System.Text.Json.Serialization;

namespace TestDocs.Cli;

internal sealed class TestRecord
{
    public string TestId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string[] Tags { get; set; } = Array.Empty<string>();

    public OriginInfo? Origin { get; set; }

    public string? Summary { get; set; }

    public string? Intent { get; set; }

    public string? Scenario { get; set; }

    public string? Behavior { get; set; }

    public string? FailureSignal { get; set; }

    public string? Risk { get; set; }

    public string? Notes { get; set; }

    public SourceInfo Source { get; set; } = new();

    public TestStatus Status { get; set; }

    public string Project { get; set; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<string> MissingRequired { get; set; } = Array.Empty<string>();
}
