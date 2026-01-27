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
/// Service collection extensions for registering time abstractions.
/// </summary>
public static class TimeServiceCollectionExtensions
{
    /// <summary>
    /// Adds time abstractions including <see cref="TimeProvider"/> and monotonic clock for the platform.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="timeProvider">Optional custom <see cref="TimeProvider"/>. If null, <see cref="TimeProvider.System"/> is used.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddTimeAbstractions(this IServiceCollection services, TimeProvider? timeProvider = null)
    {
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        return services;
    }
}
