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

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Metrics.AspNetCore;

/// <summary>
/// Endpoint registration helpers for Prometheus scraping.
/// </summary>
public static class PlatformMetricsEndpointExtensions
{
    /// <summary>
    /// Maps the Prometheus scraping endpoint on endpoint routing.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The convention builder or null when the exporter is disabled.</returns>
    public static IEndpointConventionBuilder? MapPlatformMetricsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<PlatformMetricsOptions>();
        if (!options.EnablePrometheusExporter)
        {
            return null;
        }

        return endpoints.MapPrometheusScrapingEndpoint(options.PrometheusEndpointPath);
    }

    /// <summary>
    /// Registers the Prometheus scraping endpoint in the middleware pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UsePlatformMetricsEndpoint(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetRequiredService<PlatformMetricsOptions>();
        if (!options.EnablePrometheusExporter)
        {
            return app;
        }

        return app.UseOpenTelemetryPrometheusScrapingEndpoint(options.PrometheusEndpointPath);
    }
}
