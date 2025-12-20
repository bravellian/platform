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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Threading;

namespace Bravellian.Platform.HealthChecks;

/// <summary>
/// Extension methods for adding platform-specific health check utilities.
/// </summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a cached health check, allowing status-aware cache durations.
    /// </summary>
    /// <typeparam name="THealthCheck">The health check implementation.</typeparam>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name.</param>
    /// <param name="configure">Optional configuration for caching behavior.</param>
    /// <param name="failureStatus">Failure status override.</param>
    /// <param name="tags">Tags applied to the registration.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHealthChecksBuilder AddCachedCheck<THealthCheck>(
        this IHealthChecksBuilder builder,
        string name,
        Action<CachedHealthCheckOptions>? configure = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
        where THealthCheck : class, IHealthCheck
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RegisterCachingOptions(builder, name, configure);

        var cachedHealthCheck = default(Lazy<CachedHealthCheck>);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var instance = cachedHealthCheck;
                if (instance is null)
                {
                    var created = new Lazy<CachedHealthCheck>(
                        () =>
                        {
                            var inner = ActivatorUtilities.GetServiceOrCreateInstance<THealthCheck>(sp);
                            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<CachedHealthCheckOptions>>();
                            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
                            var options = optionsMonitor.Get(name);
                            return new CachedHealthCheck(inner, options, timeProvider);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication);

                    Interlocked.CompareExchange(ref cachedHealthCheck, created, null);
                    instance = cachedHealthCheck;
                }

                return instance!.Value;
            },
            failureStatus,
            tags));
    }

    /// <summary>
    /// Adds a cached health check using a delegate.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name.</param>
    /// <param name="check">Delegate that produces a health check result.</param>
    /// <param name="configure">Optional configuration for caching behavior.</param>
    /// <param name="failureStatus">Failure status override.</param>
    /// <param name="tags">Tags applied to the registration.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHealthChecksBuilder AddCachedCheck(
        this IHealthChecksBuilder builder,
        string name,
        Func<IServiceProvider, CancellationToken, Task<HealthCheckResult>> check,
        Action<CachedHealthCheckOptions>? configure = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);

        RegisterCachingOptions(builder, name, configure);

        var cachedHealthCheck = default(Lazy<CachedHealthCheck>);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var instance = cachedHealthCheck;
                if (instance is null)
                {
                    var created = new Lazy<CachedHealthCheck>(
                        () =>
                        {
                            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<CachedHealthCheckOptions>>();
                            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
                            var options = optionsMonitor.Get(name);
                            return new CachedHealthCheck(new DelegateHealthCheck(ct => check(sp, ct)), options, timeProvider);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication);

                    Interlocked.CompareExchange(ref cachedHealthCheck, created, null);
                    instance = cachedHealthCheck;
                }

                return instance!.Value;
            },
            failureStatus,
            tags));
    }

    private static void RegisterCachingOptions(
        IHealthChecksBuilder builder,
        string name,
        Action<CachedHealthCheckOptions>? configure)
    {
        if (configure is not null)
        {
            builder.Services.Configure(name, configure);
        }
        else
        {
            builder.Services.Configure<CachedHealthCheckOptions>(name, _ => { });
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CachedHealthCheckOptions>>(new CachedHealthCheckOptionsValidator()));
        builder.Services.TryAddSingleton<IOptionsMonitorCache<CachedHealthCheckOptions>, OptionsCache<CachedHealthCheckOptions>>();
    }

    private sealed class DelegateHealthCheck : IHealthCheck
    {
        private readonly Func<CancellationToken, Task<HealthCheckResult>> check;

        public DelegateHealthCheck(Func<CancellationToken, Task<HealthCheckResult>> check)
        {
            this.check = check ?? throw new ArgumentNullException(nameof(check));
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return check(cancellationToken);
        }
    }
}
