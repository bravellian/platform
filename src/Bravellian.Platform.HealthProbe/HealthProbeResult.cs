using System.Net;

namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Represents the outcome of a health probe.
/// </summary>
public sealed class HealthProbeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HealthProbeResult"/> class.
    /// </summary>
    /// <param name="isHealthy">Whether the probe reported healthy.</param>
    /// <param name="exitCode">Process exit code to return to the caller.</param>
    /// <param name="message">Human readable message describing the result.</param>
    /// <param name="statusCode">HTTP status code, if available.</param>
    /// <param name="duration">Elapsed probe duration.</param>
    public HealthProbeResult(bool isHealthy, int exitCode, string message, HttpStatusCode? statusCode, TimeSpan duration)
    {
        IsHealthy = isHealthy;
        ExitCode = exitCode;
        Message = message;
        StatusCode = statusCode;
        Duration = duration;
    }

    /// <summary>
    /// Gets a value indicating whether the probe was healthy.
    /// </summary>
    public bool IsHealthy { get; }

    /// <summary>
    /// Gets the exit code for the probe.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets the message describing the probe outcome.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the HTTP status code returned by the endpoint.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Gets the duration of the probe.
    /// </summary>
    public TimeSpan Duration { get; }
}
