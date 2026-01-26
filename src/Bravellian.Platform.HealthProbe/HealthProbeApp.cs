using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Command-line entry points for running health probes.
/// </summary>
public static class HealthProbeApp
{
    /// <summary>
    /// Determines whether the arguments represent a health check invocation.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>True when the first argument is the health check command.</returns>
    public static bool IsHealthCheckInvocation(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 0 && args[0].Equals("healthcheck", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes a health check if invoked and returns an exit code.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="services">Service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exit code, or -1 when not invoked.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "CLI entry point maps unexpected exceptions to exit codes.")]
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
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return HealthProbeExitCodes.InvalidArguments;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return HealthProbeExitCodes.Exception;
        }
    }

    /// <summary>
    /// Runs a health check and returns the exit code.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="services">Service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exit code for the health check.</returns>
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
