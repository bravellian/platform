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

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Configuration options for the metrics exporter.
/// </summary>
public sealed class MetricsExporterOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the metrics exporter is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for minute aggregation and flush.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the size of the reservoir for percentile calculation.
    /// </summary>
    public int ReservoirSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether central hourly rollups are enabled.
    /// </summary>
    public bool EnableCentralRollup { get; set; } = true;

    /// <summary>
    /// Gets or sets the retention period for minute data in days.
    /// </summary>
    public int MinuteRetentionDays { get; set; } = 14;

    /// <summary>
    /// Gets or sets the retention period for hourly data in days.
    /// </summary>
    public int HourlyRetentionDays { get; set; } = 90;

    /// <summary>
    /// Gets or sets the service name for this instance.
    /// </summary>
    public string ServiceName { get; set; } = "Unknown";

    /// <summary>
    /// Gets or sets the connection string for the central database (for hourly rollups).
    /// </summary>
    public string? CentralConnectionString { get; set; }

    private IReadOnlySet<string>? _globalAllowedTags;

    /// <summary>
    /// Gets or sets the global set of allowed tags that apply to all metrics unless overridden.
    /// </summary>
    public IReadOnlySet<string> GlobalAllowedTags
    {
        get => _globalAllowedTags ?? DefaultGlobalAllowedTags;
        set => _globalAllowedTags = value ?? throw new ArgumentNullException(nameof(value));
    }

    private static readonly IReadOnlySet<string> DefaultGlobalAllowedTags = new HashSet<string>(StringComparer.Ordinal)
    {
        "event_type",
        "service",
        "database_id",
        "topic",
        "queue",
        "result",
        "reason",
        "kind",
        "job_name",
        "resource",
    };
}
