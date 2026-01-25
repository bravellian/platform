using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.HealthProbe;

public static class HealthProbeApp
{
    public static bool IsHealthCheckInvocation(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 0 && args[0].Equals("healthcheck", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> TryRunHealthCheckAndExitAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        if (!IsHealthCheckInvocation(args))
        {
            return -1;
        }

        try
        {
            return await RunHealthCheckAsync(args, services, cancellationToken).ConfigureAwait(false);
        }
        catch (HealthProbeArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return HealthProbeExitCodes.InvalidArguments;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return HealthProbeExitCodes.Exception;
        }
    }

    public static async Task<int> RunHealthCheckAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        var commandLine = HealthProbeCommandLine.Parse(args);
        var baseOptions = services.GetService<IOptions<HealthProbeOptions>>()?.Value ?? new HealthProbeOptions();
        var options = baseOptions.Clone();

        if (commandLine.TimeoutOverride.HasValue)
        {
            options.Timeout = commandLine.TimeoutOverride.Value;
        }

        if (commandLine.ApiKeyOverride is not null)
        {
            options.ApiKey = commandLine.ApiKeyOverride;
        }

        if (commandLine.ApiKeyHeaderNameOverride is not null)
        {
            options.ApiKeyHeaderName = commandLine.ApiKeyHeaderNameOverride;
        }

        if (commandLine.AllowInsecureTls)
        {
            options.AllowInsecureTls = true;
        }

        var resolution = HealthProbeUrlResolver.Resolve(options, commandLine.EndpointName, commandLine.UrlOverride);

        var httpClientFactory = services.GetService<IHttpClientFactory>();
        if (httpClientFactory is null)
        {
            throw new InvalidOperationException("IHttpClientFactory is not registered. Call AddBravellianHealthProbe().");
        }

        var logger = services.GetService<ILogger<HttpHealthProbeRunner>>() ?? NullLogger<HttpHealthProbeRunner>.Instance;
        var runner = new HttpHealthProbeRunner(httpClientFactory, logger, options);
        var result = await runner.RunAsync(
            new HealthProbeRequest(resolution.EndpointName, resolution.Url),
            cancellationToken).ConfigureAwait(false);

        WriteOutput(commandLine, result, resolution);
        return result.ExitCode;
    }

    private static void WriteOutput(HealthProbeCommandLine commandLine, HealthProbeResult result, HealthProbeResolution resolution)
    {
        if (commandLine.JsonOutput)
        {
            var payload = new
            {
                endpoint = resolution.EndpointName,
                url = resolution.Url.ToString(),
                status = result.IsHealthy ? "Healthy" : "Unhealthy",
                exitCode = result.ExitCode,
                httpStatus = result.StatusCode.HasValue ? (int)result.StatusCode.Value : (int?)null,
                durationMs = (int)Math.Round(result.Duration.TotalMilliseconds),
                message = result.Message,
            };

            Console.WriteLine(JsonSerializer.Serialize(payload));
            return;
        }

        var statusCode = result.StatusCode.HasValue
            ? ((int)result.StatusCode.Value).ToString(CultureInfo.InvariantCulture)
            : "n/a";
        Console.WriteLine($"{result.Message} [{resolution.EndpointName}] {resolution.Url} in {(int)Math.Round(result.Duration.TotalMilliseconds)} ms (http {statusCode})");
    }
}
