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

namespace Bravellian.Platform;
/// <summary>
/// Extension methods for unified platform registration.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds time abstractions including TimeProvider and monotonic clock for the platform.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="timeProvider">Optional custom TimeProvider. If null, TimeProvider.System is used.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddTimeAbstractions(this IServiceCollection services, TimeProvider? timeProvider = null)
    {
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        return services;
    }
}
