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

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bravellian.Platform.HealthChecks;

/// <summary>
/// Configuration options for <see cref="CachedHealthCheck"/>.
/// </summary>
public sealed class CachedHealthCheckOptions
{
    /// <summary>
    /// Gets or sets how long healthy results are cached.
    /// Defaults to 1 minute.
    /// </summary>
    public TimeSpan HealthyCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how long degraded results are cached.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DegradedCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long unhealthy results are cached.
    /// Defaults to 0 (no caching).
    /// </summary>
    public TimeSpan UnhealthyCacheDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the cache duration for a given health status.
    /// </summary>
    /// <param name="status">The health check status.</param>
    /// <returns>The cache duration for the specified status.</returns>
    internal TimeSpan GetCacheDuration(HealthStatus status)
    {
        return status switch
        {
            HealthStatus.Healthy => HealthyCacheDuration,
            HealthStatus.Degraded => DegradedCacheDuration,
            HealthStatus.Unhealthy => UnhealthyCacheDuration,
            _ => TimeSpan.Zero,
        };
    }
}
