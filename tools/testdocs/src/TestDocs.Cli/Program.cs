namespace TestDocs.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!OptionsParser.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            OptionsParser.PrintUsage();
            return 1;
        }

        var generator = new TestDocGenerator(options);
        var result = generator.Generate();

        result.WriteSummary(Console.Out);

        if (options.Strict)
        {
            if (result.Summary.MissingRequired > 0 || result.Summary.InvalidFormat > 0)
            {
                return 2;
            }

            if (result.Compliance < options.MinCompliance)
            {
                return 3;
            }
        }

        return 0;
    }
}
