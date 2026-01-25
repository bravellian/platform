namespace Bravellian.Platform.HealthProbe;

internal static class HealthProbeDefaults
{
    public const string ConfigurationRootKey = "Bravellian:HealthProbe";
    public const string DefaultApiKeyHeaderName = "X-Api-Key";
    public const string HttpClientName = "Bravellian.Platform.HealthProbe";
    public const string HttpClientInsecureName = "Bravellian.Platform.HealthProbe.Insecure";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
}
