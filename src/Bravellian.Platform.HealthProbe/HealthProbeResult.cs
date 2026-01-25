using System.Net;

namespace Bravellian.Platform.HealthProbe;

public sealed class HealthProbeResult
{
    public HealthProbeResult(bool isHealthy, int exitCode, string message, HttpStatusCode? statusCode, TimeSpan duration)
    {
        IsHealthy = isHealthy;
        ExitCode = exitCode;
        Message = message;
        StatusCode = statusCode;
        Duration = duration;
    }

    public bool IsHealthy { get; }

    public int ExitCode { get; }

    public string Message { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan Duration { get; }
}
