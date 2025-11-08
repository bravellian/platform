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

namespace Bravellian.Platform.Observability;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering platform observability services.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Adds platform observability services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>An observability builder for further configuration.</returns>
    public static ObservabilityBuilder AddPlatformObservability(
        this IServiceCollection services,
        Action<ObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<ObservabilityOptions>(_ => { });
        }

        // Register time provider if not already registered
        services.TryAddSingleton(TimeProvider.System);

        // Register watchdog service
        services.TryAddSingleton<IWatchdog, WatchdogService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(sp => (WatchdogService)sp.GetRequiredService<IWatchdog>()));

        // Register health check
        services.TryAddSingleton<WatchdogHealthCheck>();

        return new ObservabilityBuilder(services);
    }
}
