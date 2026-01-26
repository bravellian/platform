namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Describes a resolved health probe request.
/// </summary>
/// <param name="EndpointName">The configured endpoint name.</param>
/// <param name="Url">The resolved endpoint URL.</param>
public sealed record HealthProbeRequest(string EndpointName, Uri Url);
