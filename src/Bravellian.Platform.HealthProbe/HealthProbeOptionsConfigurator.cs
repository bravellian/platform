using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.HealthProbe;

internal sealed class HealthProbeOptionsConfigurator : IConfigureOptions<HealthProbeOptions>
{
    private readonly IConfiguration? configuration;

    public HealthProbeOptionsConfigurator(IConfiguration? configuration)
    {
        this.configuration = configuration;
    }

    public void Configure(HealthProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (configuration is null)
        {
            return;
        }

        var section = configuration.GetSection(HealthProbeDefaults.ConfigurationRootKey);
        if (!section.Exists())
        {
            return;
        }

        var baseUrlValue = section["BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrlValue)
            && Uri.TryCreate(baseUrlValue, UriKind.Absolute, out var baseUrl))
        {
            options.BaseUrl = baseUrl;
        }

        var defaultEndpointValue = section["DefaultEndpoint"];
        if (!string.IsNullOrWhiteSpace(defaultEndpointValue))
        {
            options.DefaultEndpoint = defaultEndpointValue;
        }

        var timeoutValue = section["TimeoutSeconds"];
        if (!string.IsNullOrWhiteSpace(timeoutValue)
            && double.TryParse(timeoutValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            options.Timeout = TimeSpan.FromSeconds(seconds);
        }

        var apiKeyValue = section["ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKeyValue))
        {
            options.ApiKey = apiKeyValue;
        }

        var headerNameValue = section["ApiKeyHeaderName"];
        if (!string.IsNullOrWhiteSpace(headerNameValue))
        {
            options.ApiKeyHeaderName = headerNameValue;
        }

        var endpointsSection = section.GetSection("Endpoints");
        if (endpointsSection.Exists())
        {
            options.Endpoints.Clear();
            foreach (var child in endpointsSection.GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Key) || string.IsNullOrWhiteSpace(child.Value))
                {
                    continue;
                }

                options.Endpoints[child.Key] = child.Value;
            }
        }
    }
}
