using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Extensions for configuring health probe services on host builders.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds health probe services to the host application builder.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The original builder for chaining.</returns>
    public static HostApplicationBuilder UseBravellianHealthProbe(
        this HostApplicationBuilder builder,
        Action<HealthProbeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddBravellianHealthProbe(configure);
        return builder;
    }
}
