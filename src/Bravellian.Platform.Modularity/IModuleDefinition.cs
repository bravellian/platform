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

using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Base contract for all modules.
/// </summary>
public interface IModuleDefinition
{
    /// <summary>
    /// Unique, URL-safe key that scopes endpoints.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Human readable name for logs and navigation.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Registers services required by the module.
    /// </summary>
    /// <param name="services">The service collection.</param>
    void AddModuleServices(IServiceCollection services);

    /// <summary>
    /// Declares required configuration keys.
    /// </summary>
    /// <returns>The required configuration keys.</returns>
    IEnumerable<string> GetRequiredConfigurationKeys();

    /// <summary>
    /// Declares optional configuration keys.
    /// </summary>
    /// <returns>The optional configuration keys.</returns>
    IEnumerable<string> GetOptionalConfigurationKeys();

    /// <summary>
    /// Receives configuration values before service registration.
    /// </summary>
    /// <param name="required">Required configuration values.</param>
    /// <param name="optional">Optional configuration values.</param>
    void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional);

    /// <summary>
    /// Registers module health checks.
    /// </summary>
    /// <param name="builder">The health check builder.</param>
    void RegisterHealthChecks(ModuleHealthCheckBuilder builder);

    /// <summary>
    /// Provides engine descriptors for the module.
    /// </summary>
    IEnumerable<IModuleEngineDescriptor> DescribeEngines();
}
