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

using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Razor Pages helpers for modules that expose UI adapters.
/// </summary>
public static class RazorModuleServiceCollectionExtensions
{
    /// <summary>
    /// Adds Razor Pages configuration for registered Razor modules.
    /// </summary>
    public static IMvcBuilder ConfigureRazorModulePages(
        this IMvcBuilder builder,
        ILoggerFactory? loggerFactory = null)
    {
        var modules = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IModuleDefinition))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRazorModule>()
            .ToArray();

        foreach (var module in modules)
        {
            builder.Services.Configure<RazorPagesOptions>(module.ConfigureRazorPages);
            builder.PartManager.ApplicationParts.Add(new AssemblyPart(module.GetType().Assembly));
            loggerFactory?.CreateLogger(typeof(RazorModuleServiceCollectionExtensions))
                .LogInformation("Registered Razor Pages for module {ModuleKey}", module.Key);
        }

        return builder;
    }
}
