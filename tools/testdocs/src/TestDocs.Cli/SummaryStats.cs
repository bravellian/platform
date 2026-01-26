namespace TestDocs.Cli;

internal sealed class SummaryStats
{
    public int Total { get; set; }

    public int Compliant { get; set; }

    public int MissingRequired { get; set; }

    public int InvalidFormat { get; set; }
}
