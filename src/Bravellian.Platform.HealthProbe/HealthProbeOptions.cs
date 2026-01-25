namespace Bravellian.Platform.HealthProbe;

public sealed class HealthProbeOptions
{
    private readonly Dictionary<string, string> endpoints = new(StringComparer.OrdinalIgnoreCase);

    public Uri? BaseUrl { get; set; }

    public string? DefaultEndpoint { get; set; }

    public IDictionary<string, string> Endpoints => endpoints;

    public TimeSpan Timeout { get; set; } = HealthProbeDefaults.DefaultTimeout;

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = HealthProbeDefaults.DefaultApiKeyHeaderName;

    public bool AllowInsecureTls { get; set; }

    internal HealthProbeOptions Clone()
    {
        var clone = new HealthProbeOptions
        {
            BaseUrl = BaseUrl,
            DefaultEndpoint = DefaultEndpoint,
            Timeout = Timeout,
            ApiKey = ApiKey,
            ApiKeyHeaderName = ApiKeyHeaderName,
            AllowInsecureTls = AllowInsecureTls,
        };

        foreach (var endpoint in endpoints)
        {
            clone.Endpoints[endpoint.Key] = endpoint.Value;
        }

        return clone;
    }
}
