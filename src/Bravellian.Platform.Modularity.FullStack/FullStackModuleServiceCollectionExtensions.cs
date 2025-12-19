using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registration helpers for full stack modules.
/// </summary>
public static class FullStackModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers services for full stack modules.
    /// </summary>
    public static IServiceCollection AddFullStackModuleServices(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
    {
        var modules = FullStackModuleRegistry.InitializeFullStackModules(configuration, services, loggerFactory);
        foreach (var module in modules)
        {
            services.AddSingleton(module.GetType(), module);
            services.AddSingleton(typeof(IApiModule), module);
            services.AddSingleton(typeof(IFullStackModule), module);
        }

        services.AddSingleton<ModuleNavigationService>();
        return services;
    }

    /// <summary>
    /// Adds Razor Pages configuration for registered full stack modules.
    /// </summary>
    public static IMvcBuilder ConfigureFullStackModuleRazorPages(
        this IMvcBuilder builder,
        ILoggerFactory? loggerFactory = null)
    {
        var modules = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IFullStackModule))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IFullStackModule>()
            .ToArray();

        foreach (var module in modules)
        {
            builder.Services.Configure<RazorPagesOptions>(module.ConfigureRazorPages);
            builder.PartManager.ApplicationParts.Add(new AssemblyPart(module.GetType().Assembly));
            loggerFactory?.CreateLogger(typeof(FullStackModuleServiceCollectionExtensions))
                .LogInformation("Registered Razor Pages for module {ModuleKey}", module.Key);
        }

        return builder;
    }
}
