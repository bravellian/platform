using System.Globalization;

namespace Bravellian.Platform.SchemaMigrations.Cli;

internal static class OptionsParser
{
    public static bool TryParse(
        string[] args,
        out SchemaMigrationOptions options,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = new SchemaMigrationOptions();
        error = string.Empty;

        if (args.Length == 0)
        {
            error = "Missing required arguments.";
            return false;
        }

        var providerSet = false;
        var connectionStringSet = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (IsHelp(arg))
            {
                options.ShowHelp = true;
                continue;
            }

            if (TryReadValue(arg, "--provider", "-p", args, ref i, out var providerValue))
            {
                if (!TryParseProvider(providerValue, out var provider, out var providerError))
                {
                    error = providerError;
                    return false;
                }

                options.Provider = provider;
                providerSet = true;
                continue;
            }

            if (TryReadValue(arg, "--connection-string", "-c", args, ref i, out var connectionValue) ||
                TryReadValue(arg, "--connection", null, args, ref i, out connectionValue))
            {
                if (string.IsNullOrWhiteSpace(connectionValue))
                {
                    error = "Connection string cannot be empty.";
                    return false;
                }

                options.ConnectionString = connectionValue;
                connectionStringSet = true;
                continue;
            }

            if (TryReadValue(arg, "--schema", "-s", args, ref i, out var schemaValue))
            {
                if (string.IsNullOrWhiteSpace(schemaValue))
                {
                    error = "Schema name cannot be empty.";
                    return false;
                }

                options.SchemaName = schemaValue;
                continue;
            }

            if (string.Equals(arg, "--include-control-plane", StringComparison.OrdinalIgnoreCase))
            {
                options.IncludeControlPlane = true;
                continue;
            }

            error = string.Format(CultureInfo.InvariantCulture, "Unknown option '{0}'.", arg);
            return false;
        }

        if (options.ShowHelp)
        {
            return true;
        }

        if (!providerSet)
        {
            error = "Missing required option '--provider'.";
            return false;
        }

        if (!connectionStringSet)
        {
            error = "Missing required option '--connection-string'.";
            return false;
        }

        return true;
    }

    public static void PrintUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("Usage:");
        writer.WriteLine("  bravellian-schema --provider <sqlserver|postgres> --connection-string <value> [--schema <name>] [--include-control-plane]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -p, --provider             Required. sqlserver or postgres.");
        writer.WriteLine("  -c, --connection-string    Required. Database connection string.");
        writer.WriteLine("  -s, --schema               Optional. Defaults to infra.");
        writer.WriteLine("      --include-control-plane Optional. Also apply control-plane bundle.");
        writer.WriteLine("  -h, --help                 Show usage.");
    }

    private static bool TryParseProvider(string? value, out SchemaProvider provider, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Provider value cannot be empty.";
            provider = default;
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("sql", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("mssql", StringComparison.OrdinalIgnoreCase))
        {
            provider = SchemaProvider.SqlServer;
            error = string.Empty;
            return true;
        }

        if (normalized.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("pg", StringComparison.OrdinalIgnoreCase))
        {
            provider = SchemaProvider.Postgres;
            error = string.Empty;
            return true;
        }

        error = string.Format(CultureInfo.InvariantCulture, "Unknown provider '{0}'.", value);
        provider = default;
        return false;
    }

    private static bool IsHelp(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadValue(
        string arg,
        string longName,
        string? shortName,
        string[] args,
        ref int index,
        out string? value)
    {
        value = null;

        if (arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(longName.Length + 1)..];
            return true;
        }

        if (shortName is not null && arg.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(shortName.Length + 1)..];
            return true;
        }

        if (string.Equals(arg, longName, StringComparison.OrdinalIgnoreCase) ||
            (shortName is not null && string.Equals(arg, shortName, StringComparison.OrdinalIgnoreCase)))
        {
            if (index + 1 >= args.Length)
            {
                value = null;
                return true;
            }

            value = args[++index];
            return true;
        }

        return false;
    }
}
