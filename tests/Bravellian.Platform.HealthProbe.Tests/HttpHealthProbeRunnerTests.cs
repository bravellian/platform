using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.HealthProbe.Tests;

public sealed class HttpHealthProbeRunnerTests
{
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
