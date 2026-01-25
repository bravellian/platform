using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.HealthProbe.Tests;

public sealed class HttpHealthProbeRunnerTests
{
    /// <summary>When the probe handler returns 200 OK, then the result is healthy.</summary>
    /// <intent>Describe how a success HTTP status maps to a healthy probe result.</intent>
    /// <scenario>Given a runner using a stub handler that returns HttpStatusCode.OK.</scenario>
    /// <behavior>The result is healthy and the exit code is Healthy.</behavior>
    [Fact]
    public async Task RunAsync_ReturnsHealthyForSuccessStatus()
    {
        var runner = CreateRunner((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeTrue();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Healthy);
    }

    /// <summary>When the probe handler returns a failure status, then the result is unhealthy.</summary>
    /// <intent>Describe how non-success HTTP status maps to an unhealthy probe result.</intent>
    /// <scenario>Given a runner using a stub handler that returns ServiceUnavailable.</scenario>
    /// <behavior>The result is unhealthy and the exit code is Unhealthy.</behavior>
    [Fact]
    public async Task RunAsync_ReturnsUnhealthyForFailureStatus()
    {
        var runner = CreateRunner((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeFalse();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Unhealthy);
    }

    /// <summary>When a 200 OK response body reports Unhealthy, then the result is unhealthy.</summary>
    /// <intent>Describe how a JSON health status maps to the probe outcome.</intent>
    /// <scenario>Given a stubbed 200 OK response with JSON status "Unhealthy".</scenario>
    /// <behavior>The result is unhealthy and the exit code is Unhealthy.</behavior>
    [Fact]
    public async Task RunAsync_ReturnsUnhealthyWhenJsonStatusIsUnhealthy()
    {
        var runner = CreateRunner((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Unhealthy\"}", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeFalse();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Unhealthy);
    }

    /// <summary>When a 200 OK response body reports Healthy, then the result is healthy.</summary>
    /// <intent>Describe how a JSON health status maps to the probe outcome.</intent>
    /// <scenario>Given a stubbed 200 OK response with JSON status "Healthy".</scenario>
    /// <behavior>The result is healthy and the exit code is Healthy.</behavior>
    [Fact]
    public async Task RunAsync_ReturnsHealthyWhenJsonStatusIsHealthy()
    {
        var runner = CreateRunner((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Healthy\"}", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeTrue();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Healthy);
    }

    /// <summary>When the HTTP status is non-success, then the result is unhealthy even if JSON says Healthy.</summary>
    /// <intent>Describe precedence of HTTP status over JSON health status.</intent>
    /// <scenario>Given a ServiceUnavailable response whose JSON body reports "Healthy".</scenario>
    /// <behavior>The result is unhealthy and the exit code is Unhealthy.</behavior>
    [Fact]
    public async Task RunAsync_DoesNotTreatNonSuccessStatusAsHealthyEvenWhenJsonIsHealthy()
    {
        var runner = CreateRunner((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"status\":\"Healthy\"}", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        });

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeFalse();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Unhealthy);
    }

    /// <summary>When the probe times out, then the result uses the exception exit code.</summary>
    /// <intent>Describe timeout handling for HTTP probes.</intent>
    /// <scenario>Given a runner with a short timeout and a handler that delays beyond it.</scenario>
    /// <behavior>The result is unhealthy and the exit code is Exception.</behavior>
    [Fact]
    public async Task RunAsync_ReturnsExceptionExitCodeOnTimeout()
    {
        var options = new HealthProbeOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var runner = CreateRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, options);

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeFalse();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Exception);
    }

    /// <summary>When API key settings are configured, then the probe request includes the API key header.</summary>
    /// <intent>Describe how API key options affect probe request headers.</intent>
    /// <scenario>Given options with ApiKey and ApiKeyHeaderName set in the runner.</scenario>
    /// <behavior>The outgoing request contains the configured header and value.</behavior>
    [Fact]
    public async Task RunAsync_AddsApiKeyHeaderWhenConfigured()
    {
        var options = new HealthProbeOptions
        {
            ApiKey = "secret",
            ApiKeyHeaderName = "X-Test-Api-Key",
        };

        var runner = CreateRunner((request, _) =>
        {
            request.Headers.Contains("X-Test-Api-Key").ShouldBeTrue();
            request.Headers.GetValues("X-Test-Api-Key").Single().ShouldBe("secret");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, options);

        var result = await runner.RunAsync(
            new HealthProbeRequest("ready", new Uri("https://example.test/ready")),
            CancellationToken.None);

        result.IsHealthy.ShouldBeTrue();
        result.ExitCode.ShouldBe(HealthProbeExitCodes.Healthy);
    }

    private static HttpHealthProbeRunner CreateRunner(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        HealthProbeOptions? options = null)
    {
        var messageHandler = new StubHttpMessageHandler(handler);
        var httpClientFactory = new TestHttpClientFactory(messageHandler);
        var runnerOptions = options ?? new HealthProbeOptions();

        return new HttpHealthProbeRunner(
            httpClientFactory,
            NullLogger<HttpHealthProbeRunner>.Instance,
            runnerOptions);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            this.handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
