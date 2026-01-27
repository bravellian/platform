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

using Bravellian.Platform.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Bravellian.Platform.Metrics.AspNetCore;

/// <summary>
/// Service registration helpers for platform metrics in ASP.NET Core.
/// </summary>
public static class PlatformMetricsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the meter provider and OpenTelemetry metrics pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for metrics options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformMetrics(
        this IServiceCollection services,
        Action<PlatformMetricsOptions>? configure = null)
    {
        var options = new PlatformMetricsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<PlatformMeterProvider>(sp =>
            new PlatformMeterProvider(
                sp.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
                options.Meter));

        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder.AddMeter(options.Meter.MeterName);

                if (options.EnableAspNetCoreInstrumentation)
                {
                    builder.AddAspNetCoreInstrumentation();
                }

                if (options.EnableRuntimeInstrumentation)
                {
                    builder.AddRuntimeInstrumentation();
                }

                if (options.EnableProcessInstrumentation)
                {
                    builder.AddProcessInstrumentation();
                }

                if (options.EnablePrometheusExporter)
                {
                    builder.AddPrometheusExporter(exporterOptions =>
                    {
                        exporterOptions.ScrapeEndpointPath = options.PrometheusEndpointPath;
                        exporterOptions.ScrapeResponseCacheDurationMilliseconds =
                            options.PrometheusScrapeResponseCacheMilliseconds;
                    });
                }
            });

        return services;
    }
}
