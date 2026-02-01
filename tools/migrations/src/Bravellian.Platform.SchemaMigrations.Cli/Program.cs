namespace Bravellian.Platform.SchemaMigrations.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OptionsParser.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            OptionsParser.PrintUsage(Console.Error);
            return 1;
        }

        if (options.ShowHelp)
        {
            OptionsParser.PrintUsage(Console.Out);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            Console.WriteLine($"Running {options.Provider} schema migrations for schema '{options.SchemaName}'.");
            if (options.IncludeControlPlane)
            {
                Console.WriteLine("Including control-plane bundle.");
            }

            switch (options.Provider)
            {
                case SchemaProvider.SqlServer:
                    await SqlServerSchemaMigrator
                        .ApplyLatestAsync(
                            options.ConnectionString,
                            options.SchemaName,
                            options.IncludeControlPlane,
                            cancellation.Token)
                        .ConfigureAwait(false);
                    break;
                case SchemaProvider.Postgres:
                    await PostgresSchemaMigrator
                        .ApplyLatestAsync(
                            options.ConnectionString,
                            options.SchemaName,
                            options.IncludeControlPlane,
                            cancellation.Token)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported provider.");
            }

            Console.WriteLine("Schema migrations completed.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Schema migrations canceled.").ConfigureAwait(false);
            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 3;
        }
    }
}
