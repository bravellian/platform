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

namespace Bravellian.Platform.Metrics.AspNetCore;

/// <summary>
/// Configures instrumentation and Prometheus exposure for ASP.NET Core.
/// </summary>
public sealed class PlatformMetricsOptions
{
    /// <summary>
    /// Gets or sets options for the underlying meter.
    /// </summary>
    public PlatformMeterOptions Meter { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether ASP.NET Core instrumentation is enabled.
    /// </summary>
    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether runtime instrumentation is enabled.
    /// </summary>
    public bool EnableRuntimeInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether process instrumentation is enabled.
    /// </summary>
    public bool EnableProcessInstrumentation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Prometheus exporter is enabled.
    /// </summary>
    public bool EnablePrometheusExporter { get; set; }

    /// <summary>
    /// Gets or sets the path for the Prometheus scrape endpoint.
    /// </summary>
    public string PrometheusEndpointPath { get; set; } = "/metrics";

    /// <summary>
    /// Gets or sets the cache duration (milliseconds) for scrape responses.
    /// </summary>
    public int PrometheusScrapeResponseCacheMilliseconds { get; set; } = 300;
}
