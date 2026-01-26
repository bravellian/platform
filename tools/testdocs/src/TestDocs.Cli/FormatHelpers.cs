namespace TestDocs.Cli;

internal static class FormatHelpers
{
    public static string FormatPercent(double value)
    {
        var percent = value * 100;
        return FormattableString.Invariant($"{percent:0.0}%");
    }
}
