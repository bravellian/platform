namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Configuration options for health probe execution.
/// </summary>
public sealed class HealthProbeOptions
{
    private readonly Dictionary<string, string> endpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the base URL used to resolve relative endpoints.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the default endpoint name.
    /// </summary>
    public string? DefaultEndpoint { get; set; }

    /// <summary>
    /// Gets the configured endpoint map keyed by endpoint name.
    /// </summary>
    public IDictionary<string, string> Endpoints => endpoints;

    /// <summary>
    /// Gets or sets the health probe timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = HealthProbeDefaults.DefaultTimeout;

    /// <summary>
    /// Gets or sets the API key used for authenticated probes.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the header name for the API key.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = HealthProbeDefaults.DefaultApiKeyHeaderName;

    /// <summary>
    /// Gets or sets a value indicating whether insecure TLS is allowed for probing.
    /// </summary>
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
