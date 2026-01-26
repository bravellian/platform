using System.Globalization;

namespace TestDocs.Cli;

internal static class OptionsParser
{
    public static bool TryParse(string[] args, out Options options, out string error)
    {
        options = null!;
        error = string.Empty;

        try
        {
            if (args.Length == 0 || string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
            {
                error = "Missing command.";
                return false;
            }

            if (!string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
            {
                error = "Unknown command. Expected 'generate'.";
                return false;
            }

            var repoRoot = Directory.GetCurrentDirectory();
            var outDir = Path.Combine(repoRoot, "docs", "testing", "generated");
            var strict = false;
            var minCompliance = 0.95;
            var format = OutputFormat.Both;

            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--repoRoot":
                        repoRoot = GetValue(args, ref i, "--repoRoot");
                        break;
                    case "--outDir":
                        outDir = GetValue(args, ref i, "--outDir");
                        break;
                    case "--strict":
                        strict = true;
                        break;
                    case "--minCompliance":
                        var value = GetValue(args, ref i, "--minCompliance");
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out minCompliance))
                        {
                            error = "--minCompliance must be a number between 0 and 1.";
                            return false;
                        }

                        if (minCompliance < 0 || minCompliance > 1)
                        {
                            error = "--minCompliance must be between 0 and 1.";
                            return false;
                        }
                        break;
                    case "--format":
                        var formatValue = GetValue(args, ref i, "--format");
                        format = formatValue.ToLowerInvariant() switch
                        {
                            "markdown" => OutputFormat.Markdown,
                            "json" => OutputFormat.Json,
                            "both" => OutputFormat.Both,
                            _ => throw new ArgumentException("Invalid format. Use markdown, json, or both."),
                        };
                        break;
                    default:
                        error = $"Unknown argument '{arg}'.";
                        return false;
                }
            }

            repoRoot = Path.GetFullPath(repoRoot);
            outDir = Path.GetFullPath(Path.IsPathRooted(outDir) ? outDir : Path.Combine(repoRoot, outDir));

            options = new Options
            {
                RepoRoot = repoRoot,
                OutDir = outDir,
                Strict = strict,
                MinCompliance = minCompliance,
                Format = format,
            };

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project tools/testdocs/src/TestDocs.Cli -- generate [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --repoRoot <path>       Repository root (default: current directory)");
        Console.WriteLine("  --outDir <path>         Output directory (default: docs/testing/generated)");
        Console.WriteLine("  --strict                Fail when missing/invalid metadata or compliance below threshold");
        Console.WriteLine("  --minCompliance <0-1>   Compliance threshold (default: 0.95)");
        Console.WriteLine("  --format <markdown|json|both> (default: both)");
    }

    private static string GetValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }
}
