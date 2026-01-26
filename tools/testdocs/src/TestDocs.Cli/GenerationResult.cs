namespace TestDocs.Cli;

internal sealed class GenerationResult
{
    public GenerationResult(StatsReport report, double compliance)
    {
        Report = report;
        Compliance = compliance;
    }

    public StatsReport Report { get; }

    public SummaryStats Summary => Report.Summary;

    public double Compliance { get; }

    public void WriteSummary(TextWriter writer)
    {
        writer.WriteLine(FormattableString.Invariant($"Test docs: {Summary.Total} tests, {Summary.Compliant} compliant, {Summary.MissingRequired} missing required, {Summary.InvalidFormat} invalid."));
        writer.WriteLine($"Compliance: {FormatHelpers.FormatPercent(Compliance)}");
    }
}
