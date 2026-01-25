using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.HealthProbe.Tests;

public sealed class HealthProbeCommandLineTests
{
    [Fact]
    public void Parse_DefaultsToConfiguredEndpoint()
    {
        var commandLine = HealthProbeCommandLine.Parse(new[] { "healthcheck" });

        commandLine.EndpointName.ShouldBeNull();
        commandLine.JsonOutput.ShouldBeFalse();
    }

    [Fact]
    public void Parse_ExplicitEndpointName()
    {
        var commandLine = HealthProbeCommandLine.Parse(new[] { "healthcheck", "deploy" });

        commandLine.EndpointName.ShouldBe("deploy");
    }

    [Fact]
    public void Parse_Overrides()
    {
        var commandLine = HealthProbeCommandLine.Parse(new[]
        {
            "healthcheck",
            "ready",
            "--url",
            "https://example.test/health",
            "--timeout",
            "5",
            "--header",
            "X-Test",
            "--apikey",
            "secret",
            "--insecure",
            "--json",
        });

        commandLine.EndpointName.ShouldBe("ready");
        commandLine.UrlOverride.ShouldNotBeNull();
        commandLine.UrlOverride!.ToString().ShouldBe("https://example.test/health");
        commandLine.TimeoutOverride.ShouldBe(TimeSpan.FromSeconds(5));
        commandLine.ApiKeyHeaderNameOverride.ShouldBe("X-Test");
        commandLine.ApiKeyOverride.ShouldBe("secret");
        commandLine.AllowInsecureTls.ShouldBeTrue();
        commandLine.JsonOutput.ShouldBeTrue();
    }

    [Fact]
    public async Task TryRun_ReturnsInvalidWhenUrlMissing()
    {
        var services = new ServiceCollection()
            .AddBravellianHealthProbe()
            .BuildServiceProvider();

        var exitCode = await HealthProbeApp.TryRunHealthCheckAndExitAsync(
            new[] { "healthcheck" },
            services,
            CancellationToken.None);

        exitCode.ShouldBe(HealthProbeExitCodes.InvalidArguments);
    }

    [Fact]
    public void Parse_ThrowsForUnknownFlag()
    {
        var exception = Should.Throw<HealthProbeArgumentException>(() =>
            HealthProbeCommandLine.Parse(new[] { "healthcheck", "--nope" }));

        exception.Message.ShouldContain("Unknown option");
    }
}
