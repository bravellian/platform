using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.HealthProbe.Tests;

/// <summary>
/// Tests for health probe command-line parsing and execution.
/// </summary>
public sealed class HealthProbeCommandLineTests
{
    private static readonly string[] DefaultArgs = { "healthcheck" };
    private static readonly string[] DeployArgs = { "healthcheck", "deploy" };
    private static readonly string[] UnknownFlagArgs = { "healthcheck", "--nope" };
    private static readonly string[] OverridesArgs =
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
    };
    /// <summary>Given only the base command, then parsing leaves EndpointName null and JsonOutput false.</summary>
    /// <intent>Describe default parsing with no endpoint or flags.</intent>
    /// <scenario>Given arguments containing only "healthcheck".</scenario>
    /// <behavior>EndpointName remains null and JsonOutput stays false.</behavior>
    [Fact]
    public void ParseDefaultsToConfiguredEndpoint()
    {
        var commandLine = HealthProbeCommandLine.Parse(DefaultArgs);

        commandLine.EndpointName.ShouldBeNull();
        commandLine.JsonOutput.ShouldBeFalse();
    }

    /// <summary>Given an explicit endpoint argument, then parsing uses it as EndpointName.</summary>
    /// <intent>Describe parsing of a positional endpoint argument.</intent>
    /// <scenario>Given arguments "healthcheck" and "deploy".</scenario>
    /// <behavior>EndpointName is "deploy".</behavior>
    [Fact]
    public void ParseUsesExplicitEndpointName()
    {
        var commandLine = HealthProbeCommandLine.Parse(DeployArgs);

        commandLine.EndpointName.ShouldBe("deploy");
    }

    /// <summary>When override flags are provided, then parsing populates each override option.</summary>
    /// <intent>Describe parsing of URL, timeout, header, API key, TLS, and JSON flags.</intent>
    /// <scenario>Given arguments with url, timeout, header, apikey, insecure, and json options.</scenario>
    /// <behavior>EndpointName and all override properties match the provided values.</behavior>
    [Fact]
    public void ParseUsesOverrides()
    {
        var commandLine = HealthProbeCommandLine.Parse(OverridesArgs);

        commandLine.EndpointName.ShouldBe("ready");
        commandLine.UrlOverride.ShouldNotBeNull();
        commandLine.UrlOverride!.ToString().ShouldBe("https://example.test/health");
        commandLine.TimeoutOverride.ShouldBe(TimeSpan.FromSeconds(5));
        commandLine.ApiKeyHeaderNameOverride.ShouldBe("X-Test");
        commandLine.ApiKeyOverride.ShouldBe("secret");
        commandLine.AllowInsecureTls.ShouldBeTrue();
        commandLine.JsonOutput.ShouldBeTrue();
    }

    /// <summary>Given no URL override, then running the health check returns InvalidArguments.</summary>
    /// <intent>Describe validation when required URL input is missing.</intent>
    /// <scenario>Given a service provider and a command line containing only "healthcheck".</scenario>
    /// <behavior>The returned exit code is InvalidArguments.</behavior>
    [Fact]
    public async Task TryRunReturnsInvalidWhenUrlMissing()
    {
        var services = new ServiceCollection()
            .AddBravellianHealthProbe()
            .BuildServiceProvider();

        var exitCode = await HealthProbeApp.TryRunHealthCheckAndExitAsync(
            DefaultArgs,
            services,
            CancellationToken.None);

        exitCode.ShouldBe(HealthProbeExitCodes.InvalidArguments);
    }

    /// <summary>When an unknown flag is provided, then parsing throws a HealthProbeArgumentException.</summary>
    /// <intent>Describe parsing failure for unsupported command line options.</intent>
    /// <scenario>Given arguments including an unrecognized "--nope" option.</scenario>
    /// <behavior>Parsing throws and the message mentions an unknown option.</behavior>
    [Fact]
    public void ParseThrowsForUnknownFlag()
    {
        var exception = Should.Throw<HealthProbeArgumentException>(() =>
            HealthProbeCommandLine.Parse(UnknownFlagArgs));

        exception.Message.ShouldContain("Unknown option");
    }
}
