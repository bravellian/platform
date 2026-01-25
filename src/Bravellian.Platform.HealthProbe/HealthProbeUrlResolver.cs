namespace Bravellian.Platform.HealthProbe;

internal static class HealthProbeUrlResolver
{
    public static HealthProbeResolution Resolve(HealthProbeOptions options, string? endpointName, Uri? overrideUrl)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (overrideUrl is not null)
        {
            if (!overrideUrl.IsAbsoluteUri)
            {
                throw new HealthProbeArgumentException("Health probe URL must be absolute.");
            }

            var overrideName = string.IsNullOrWhiteSpace(endpointName) ? "custom" : endpointName;
            return new HealthProbeResolution(overrideName, overrideUrl);
        }

        var resolvedName = ResolveEndpointName(options, endpointName);
        if (!options.Endpoints.TryGetValue(resolvedName, out var endpointValue)
            || string.IsNullOrWhiteSpace(endpointValue))
        {
            throw new HealthProbeArgumentException($"Health probe endpoint '{resolvedName}' is not configured.");
        }

        if (Uri.TryCreate(endpointValue, UriKind.Absolute, out var absoluteEndpoint))
        {
            return new HealthProbeResolution(resolvedName, absoluteEndpoint);
        }

        if (options.BaseUrl is null)
        {
            throw new HealthProbeArgumentException("Health probe base URL is not configured.");
        }

        if (!options.BaseUrl.IsAbsoluteUri)
        {
            throw new HealthProbeArgumentException("Health probe base URL must be absolute.");
        }

        var path = NormalizePath(endpointValue);
        var resolvedUri = new Uri(options.BaseUrl, path);
        return new HealthProbeResolution(resolvedName, resolvedUri);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        return path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
    }

    private static string ResolveEndpointName(HealthProbeOptions options, string? endpointName)
    {
        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            return endpointName;
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultEndpoint))
        {
            return options.DefaultEndpoint;
        }

        if (options.Endpoints.Count == 1)
        {
            return options.Endpoints.Keys.Single();
        }

        throw new HealthProbeArgumentException("Health probe endpoint name is required.");
    }
}
