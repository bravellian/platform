namespace Bravellian.Platform.HealthProbe.Tests;

/// <summary>
/// Tests for health probe URL resolution behavior.
/// </summary>
public sealed class HealthProbeUrlResolverTests
{
    /// <summary>Given a base URL with no path and a default ready endpoint, then resolution appends /ready.</summary>
    /// <intent>Describe URL resolution for the default endpoint.</intent>
    /// <scenario>Given a base URL without a path, a ready endpoint path, and no override URL.</scenario>
    /// <behavior>The resolved URL is https://example.test/ready and EndpointName is ready.</behavior>
    [Fact]
    public void ResolveAppendsReadyPathWhenBaseHasNoPath()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["ready"] = "/ready";
        options.DefaultEndpoint = "ready";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: null, overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/ready");
        resolved.EndpointName.ShouldBe("ready");
    }

    /// <summary>When resolving the live endpoint against a base URL with no path, then /live is appended.</summary>
    /// <intent>Describe URL resolution for an explicit endpoint name.</intent>
    /// <scenario>Given a base URL without a path and a live endpoint mapping.</scenario>
    /// <behavior>The resolved URL is https://example.test/live.</behavior>
    [Fact]
    public void ResolveAppendsLivePathWhenBaseHasNoPath()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["live"] = "/live";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: "live", overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/live");
    }

    /// <summary>Given an endpoint mapped to an absolute URL, then resolution uses that URL.</summary>
    /// <intent>Describe resolution behavior for absolute endpoint URLs.</intent>
    /// <scenario>Given an endpoint configured with https://example.test/healthz.</scenario>
    /// <behavior>The resolved URL matches the configured absolute URL.</behavior>
    [Fact]
    public void ResolveUsesExplicitPathWhenProvided()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["deploy"] = "https://example.test/healthz";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: "deploy", overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/healthz");
    }

    /// <summary>When the endpoint path is relative, then resolution combines it with the base URL.</summary>
    /// <intent>Describe resolution behavior for relative endpoint paths.</intent>
    /// <scenario>Given a base URL and a ready endpoint path without a leading slash.</scenario>
    /// <behavior>The resolved URL is https://example.test/readyz.</behavior>
    [Fact]
    public void ResolveNormalizesPathWhenReadyPathIsRelative()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["ready"] = "readyz";
        options.DefaultEndpoint = "ready";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: null, overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/readyz");
    }

    /// <summary>When an override URL is provided, then resolution returns the override and preserves the endpoint name.</summary>
    /// <intent>Describe precedence of override URLs.</intent>
    /// <scenario>Given a configured endpoint and an explicit override URL.</scenario>
    /// <behavior>The resolved URL matches the override and EndpointName remains "deploy".</behavior>
    [Fact]
    public void ResolveUsesOverrideUrlWhenProvided()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["ready"] = "/ready";
        options.DefaultEndpoint = "ready";

        var overrideUrl = new Uri("https://override.test/custom");
        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: "deploy", overrideUrl);

        resolved.EndpointName.ShouldBe("deploy");
        resolved.Url.ToString().ShouldBe("https://override.test/custom");
    }
}
