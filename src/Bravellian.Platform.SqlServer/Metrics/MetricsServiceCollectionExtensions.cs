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
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Extension methods for registering metrics exporter services.
/// </summary>
public static class MetricsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the metrics exporter service to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMetricsExporter(
        this IServiceCollection services,
        Action<MetricsExporterOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Register the metric registrar as singleton
        services.AddSingleton<MetricRegistrar>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MetricRegistrar>>();
            var registrar = new MetricRegistrar(logger);

            // Automatically register platform metrics
            registrar.RegisterRange(PlatformMetricCatalog.All);

            return registrar;
        });
        services.AddSingleton<IMetricRegistrar>(sp => sp.GetRequiredService<MetricRegistrar>());

        services.AddSingleton<MetricsExporterService>();
        services.AddHostedService(sp => sp.GetRequiredService<MetricsExporterService>());

        return services;
    }

    /// <summary>
    /// Adds the metrics exporter health check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMetricsExporterHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<MetricsExporterHealthCheck>("metrics_exporter", tags: new[] { "metrics", "platform" });

        return services;
    }
}
