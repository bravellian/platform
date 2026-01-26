namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Executes health probe requests.
/// </summary>
public interface IHealthProbeRunner
{
    /// <summary>
    /// Runs a health probe request.
    /// </summary>
    /// <param name="request">The probe request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The probe result.</returns>
    Task<HealthProbeResult> RunAsync(HealthProbeRequest request, CancellationToken cancellationToken);
}
