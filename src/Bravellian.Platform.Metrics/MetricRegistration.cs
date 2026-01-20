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
/// Represents a metric registration with allowed tags.
/// </summary>
public sealed record MetricRegistration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetricRegistration"/> record.
    /// </summary>
    /// <param name="name">The metric name (e.g., "outbox.published.count").</param>
    /// <param name="unit">The unit of measurement (e.g., MetricUnit.Count, MetricUnit.Milliseconds).</param>
    /// <param name="aggKind">The aggregation kind.</param>
    /// <param name="description">A human-readable description of the metric.</param>
    /// <param name="allowedTags">An array of tag keys that are allowed for this metric.</param>
    public MetricRegistration(
        string name,
        string unit,
        MetricAggregationKind aggKind,
        string description,
        string[] allowedTags)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Metric name cannot be null or whitespace", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("Unit cannot be null or whitespace", nameof(unit));
        }

        if (!Enum.IsDefined(typeof(MetricAggregationKind), aggKind))
        {
            throw new ArgumentException($"Invalid aggregation kind: {aggKind}", nameof(aggKind));
        }

        Name = name;
        Unit = unit;
        AggKind = aggKind;
        Description = description ?? string.Empty;
        AllowedTags = allowedTags ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the metric name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the unit of measurement.
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// Gets the aggregation kind.
    /// </summary>
    public MetricAggregationKind AggKind { get; }

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the allowed tags.
    /// </summary>
    public string[] AllowedTags { get; }
}
