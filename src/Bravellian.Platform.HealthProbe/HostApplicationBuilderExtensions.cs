using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform.HealthProbe;

public static class HostApplicationBuilderExtensions
{
    public static HostApplicationBuilder UseBravellianHealthProbe(
        this HostApplicationBuilder builder,
        Action<HealthProbeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddBravellianHealthProbe(configure);
        return builder;
    }
}
