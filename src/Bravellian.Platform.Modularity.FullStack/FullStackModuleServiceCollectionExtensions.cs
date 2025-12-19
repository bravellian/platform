// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

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
            services.AddSingleton<IApiModule>(module);
            services.AddSingleton(module);
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
