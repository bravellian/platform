using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Extensions for registering health probe services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers health probe services in the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBravellianHealthProbe(
        this IServiceCollection services,
        Action<HealthProbeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<HealthProbeOptions>();
        services.AddSingleton<IConfigureOptions<HealthProbeOptions>>(static serviceProvider =>
        {
            return new HealthProbeOptionsConfigurator(serviceProvider.GetService<IConfiguration>());
        });

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHttpClient(HealthProbeDefaults.HttpClientName);
        #pragma warning disable MA0039 // Healthcheck CLI optionally allows insecure TLS for local diagnostics.
        services.AddHttpClient(HealthProbeDefaults.HttpClientInsecureName)
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        #pragma warning restore MA0039

        services.AddTransient<IHealthProbeRunner>(static serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HealthProbeOptions>>().Value;
            return new HttpHealthProbeRunner(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<ILogger<HttpHealthProbeRunner>>(),
                options);
        });

        return services;
    }

}
