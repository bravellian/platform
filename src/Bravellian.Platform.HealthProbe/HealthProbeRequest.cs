namespace Bravellian.Platform.HealthProbe;

public sealed record HealthProbeRequest(string EndpointName, Uri Url);
