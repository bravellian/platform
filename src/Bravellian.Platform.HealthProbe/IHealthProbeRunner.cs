namespace Bravellian.Platform.HealthProbe;

public interface IHealthProbeRunner
{
    Task<HealthProbeResult> RunAsync(HealthProbeRequest request, CancellationToken cancellationToken);
}
