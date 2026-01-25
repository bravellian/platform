namespace Bravellian.Platform.HealthProbe.Tests;

public sealed class HealthProbeUrlResolverTests
{
    [Fact]
    public void Resolve_AppendsReadyPathWhenBaseHasNoPath()
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

    [Fact]
    public void Resolve_AppendsLivePathWhenBaseHasNoPath()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["live"] = "/live";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: "live", overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/live");
    }

    [Fact]
    public void Resolve_UsesExplicitPathWhenProvided()
    {
        var options = new HealthProbeOptions
        {
            BaseUrl = new Uri("https://example.test"),
        };
        options.Endpoints["deploy"] = "https://example.test/healthz";

        var resolved = HealthProbeUrlResolver.Resolve(options, endpointName: "deploy", overrideUrl: null);

        resolved.Url.ToString().ShouldBe("https://example.test/healthz");
    }

    [Fact]
    public void Resolve_NormalizesPathWhenReadyPathIsRelative()
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

    [Fact]
    public void Resolve_UsesOverrideUrlWhenProvided()
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
