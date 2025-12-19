using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registration helpers for API modules.
/// </summary>
public static class ApiModuleRegistry
{
    /// <summary>
    /// Registers an API module type.
    /// </summary>
    public static void RegisterApiModule<T>() where T : class, IApiModule, new()
    {
        ModuleRegistry.RegisterModuleType(typeof(T), ModuleCategory.Api);
    }

    internal static IReadOnlyCollection<IApiModule> InitializeApiModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return ModuleRegistry.InitializeModules<IApiModule>(ModuleCategory.Api, configuration, services, loggerFactory);
    }
}
