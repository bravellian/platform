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
