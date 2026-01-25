using System.Globalization;

namespace Bravellian.Platform.HealthProbe;

internal sealed class HealthProbeCommandLine
{
    private HealthProbeCommandLine(
        string? endpointName,
        Uri? urlOverride,
        TimeSpan? timeoutOverride,
        string? apiKeyOverride,
        string? apiKeyHeaderNameOverride,
        bool allowInsecureTls,
        bool jsonOutput)
    {
        EndpointName = endpointName;
        UrlOverride = urlOverride;
        TimeoutOverride = timeoutOverride;
        ApiKeyOverride = apiKeyOverride;
        ApiKeyHeaderNameOverride = apiKeyHeaderNameOverride;
        AllowInsecureTls = allowInsecureTls;
        JsonOutput = jsonOutput;
    }

    public string? EndpointName { get; }

    public Uri? UrlOverride { get; }

    public TimeSpan? TimeoutOverride { get; }

    public string? ApiKeyOverride { get; }

    public string? ApiKeyHeaderNameOverride { get; }

    public bool AllowInsecureTls { get; }

    public bool JsonOutput { get; }

    public static HealthProbeCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new HealthProbeArgumentException("Missing command. Expected 'healthcheck'.");
        }

        if (!HealthProbeApp.IsHealthCheckInvocation(args))
        {
            throw new HealthProbeArgumentException("Missing 'healthcheck' command.");
        }

        string? endpointName = null;
        Uri? urlOverride = null;
        TimeSpan? timeoutOverride = null;
        string? apiKeyOverride = null;
        string? apiKeyHeaderNameOverride = null;
        var allowInsecureTls = false;
        var jsonOutput = false;

        var index = 1;
        if (index < args.Length && !IsFlag(args[index]))
        {
            endpointName = args[index];
            index++;
        }

        while (index < args.Length)
        {
            var token = args[index];
            if (!IsFlag(token))
            {
                throw new HealthProbeArgumentException($"Unexpected argument '{token}'.");
            }

            switch (token)
            {
                case "--url":
                    urlOverride = ParseUrl(RequireValue(args, ref index, token));
                    break;
                case "--timeout":
                    timeoutOverride = ParseTimeout(RequireValue(args, ref index, token));
                    break;
                case "--header":
                    apiKeyHeaderNameOverride = RequireValue(args, ref index, token);
                    break;
                case "--apikey":
                    apiKeyOverride = RequireValue(args, ref index, token);
                    break;
                case "--insecure":
                    allowInsecureTls = true;
                    index++;
                    break;
                case "--json":
                    jsonOutput = true;
                    index++;
                    break;
                default:
                    throw new HealthProbeArgumentException($"Unknown option '{token}'.");
            }
        }

        return new HealthProbeCommandLine(
            endpointName,
            urlOverride,
            timeoutOverride,
            apiKeyOverride,
            apiKeyHeaderNameOverride,
            allowInsecureTls,
            jsonOutput);
    }

    private static bool IsFlag(string value)
    {
        return value.StartsWith("--", StringComparison.Ordinal);
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new HealthProbeArgumentException($"Missing value for '{optionName}'.");
        }

        var value = args[index + 1];
        if (IsFlag(value))
        {
            throw new HealthProbeArgumentException($"Missing value for '{optionName}'.");
        }

        index += 2;
        return value;
    }

    private static Uri ParseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new HealthProbeArgumentException($"Invalid URL '{value}'.");
        }

        return uri;
    }

    private static TimeSpan ParseTimeout(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new HealthProbeArgumentException($"Invalid timeout '{value}'.");
        }

        if (seconds <= 0)
        {
            throw new HealthProbeArgumentException("Timeout must be greater than zero.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
