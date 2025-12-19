using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registration helpers for full stack modules.
/// </summary>
public static class FullStackModuleRegistry
{
    /// <summary>
    /// Registers a full stack module type.
    /// </summary>
    public static void RegisterFullStackModule<T>() where T : class, IFullStackModule, new()
    {
        ModuleRegistry.RegisterModuleType(typeof(T), ModuleCategory.FullStack);
    }

    internal static IReadOnlyCollection<IFullStackModule> InitializeFullStackModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return ModuleRegistry.InitializeModules<IFullStackModule>(ModuleCategory.FullStack, configuration, services, loggerFactory);
    }
}
