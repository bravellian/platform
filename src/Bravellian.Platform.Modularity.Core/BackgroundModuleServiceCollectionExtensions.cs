using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registration helpers for background modules.
/// </summary>
public static class BackgroundModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers services for background modules.
    /// </summary>
    public static IServiceCollection AddBackgroundModuleServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
    {
        ModuleRegistry.InitializeModules<IBackgroundModule>(ModuleCategory.Background, configuration, services, loggerFactory);
        return services;
    }
}
