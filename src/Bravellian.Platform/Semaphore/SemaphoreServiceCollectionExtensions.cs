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

namespace Bravellian.Platform.Semaphore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for registering semaphore services.
/// </summary>
internal static class SemaphoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers semaphore services for control-plane environments only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneConnectionString">The control plane connection string.</param>
    /// <param name="configure">Optional configuration action.</param>
    internal static void AddSemaphoreServices(
        this IServiceCollection services,
        string controlPlaneConnectionString,
        Action<SemaphoreOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneConnectionString);

        services.AddOptions<SemaphoreOptions>()
            .Configure(options =>
            {
                options.ControlPlaneConnectionString = controlPlaneConnectionString;
                configure?.Invoke(options);
            })
            .ValidateOnStart();

        services.AddSingleton<ISemaphoreService, SqlSemaphoreService>();
        services.AddSingleton<IHostedService, SemaphoreReaperService>();
    }
}
