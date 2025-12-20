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
using System.Threading;

namespace Bravellian.Platform.HealthChecks;

/// <summary>
/// Wraps an existing health check with caching behavior that respects status-specific durations.
/// </summary>
public sealed class CachedHealthCheck : IHealthCheck, IDisposable
{
    private readonly IHealthCheck innerHealthCheck;
    private readonly CachedHealthCheckOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    private HealthCheckResult? cachedResult;
    private DateTimeOffset lastCheckTime = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedHealthCheck"/> class.
    /// </summary>
    /// <param name="innerHealthCheck">The health check to wrap.</param>
    /// <param name="options">Caching options.</param>
    /// <param name="timeProvider">Optional time provider for testing.</param>
    public CachedHealthCheck(IHealthCheck innerHealthCheck, CachedHealthCheckOptions options, TimeProvider? timeProvider = null)
    {
        this.innerHealthCheck = innerHealthCheck ?? throw new ArgumentNullException(nameof(innerHealthCheck));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // First check without holding lock (optimistic read)
        var currentCachedResult = cachedResult;
        var currentLastCheckTime = lastCheckTime;

        if (ShouldUseCache(currentCachedResult, currentLastCheckTime, now))
        {
            return currentCachedResult!.Value;
        }

        // Cache miss - acquire lock for execution
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check pattern: verify cache is still stale after acquiring lock
            if (ShouldUseCache(cachedResult, lastCheckTime, timeProvider.GetUtcNow()))
            {
                return cachedResult!.Value;
            }

            // Execute health check while holding lock to prevent concurrent execution
            var result = await innerHealthCheck.CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);

            cachedResult = result;
            lastCheckTime = timeProvider.GetUtcNow();

            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        semaphore.Dispose();
    }

    private bool ShouldUseCache(HealthCheckResult? result, DateTimeOffset lastCheck, DateTimeOffset now)
    {
        if (result is null)
        {
            return false;
        }

        var elapsed = now - lastCheck;
        var cacheDuration = options.GetCacheDuration(result.Value.Status);

        return elapsed < cacheDuration;
    }
}
