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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform;

/// <summary>
/// Extension methods for registering the startup check runner.
/// </summary>
public static class StartupCheckRunnerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the startup check runner hosted service and its dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStartupCheckRunner(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddStartupLatch();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupCheckRunnerHostedService>());

        return services;
    }
}
