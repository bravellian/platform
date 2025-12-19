using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registration helpers for API modules.
/// </summary>
public static class ApiModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers services for API modules.
    /// </summary>
    public static IServiceCollection AddApiModuleServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
    {
        var modules = ApiModuleRegistry.InitializeApiModules(configuration, services, loggerFactory)
            .ToArray();

        if (modules.Length == 0)
        {
            modules = ModuleRegistry.GetModules<IApiModule>().ToArray();
        }

        if (modules.Length == 0)
        {
            throw new InvalidOperationException(
                "No API modules have been registered. Call ApiModuleRegistry.RegisterApiModule<TModule>() before adding services.");
        }
        foreach (var module in modules)
        {
            services.AddSingleton(module.GetType(), module);
            services.AddSingleton(typeof(IApiModule), module);
        }

        return services;
    }

    /// <summary>
    /// Maps endpoints for API and full stack modules.
    /// </summary>
    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        foreach (var module in app.Services.GetServices<IApiModule>())
        {
            var group = app.MapGroup($"/{module.Key}");
            module.MapApiEndpoints(group);
        }

        return app;
    }
}
