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

    /// <summary>
    /// Gets a snapshot of all registered API module types.
    /// </summary>
    /// <returns>A read-only collection of registered API module types.</returns>
    public static IReadOnlyCollection<Type> GetRegisteredModuleTypes()
    {
        return ModuleRegistry.GetRegisteredTypes(ModuleCategory.Api);
    }

    internal static IReadOnlyCollection<IApiModule> InitializeApiModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return ModuleRegistry.InitializeModules<IApiModule>(ModuleCategory.Api, configuration, services, loggerFactory);
    }
}
