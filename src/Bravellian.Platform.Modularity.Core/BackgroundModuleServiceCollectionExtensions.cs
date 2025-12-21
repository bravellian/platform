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
        services.AddSingleton<ModuleEngineDiscoveryService>();
        return services;
    }
}
