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
/// <param name="Name">The metric name (e.g., "outbox.published.count").</param>
/// <param name="Unit">The unit of measurement (e.g., "count", "ms", "seconds").</param>
/// <param name="AggKind">The aggregation kind ("counter", "gauge", or "histogram").</param>
/// <param name="Description">A human-readable description of the metric.</param>
/// <param name="AllowedTags">An array of tag keys that are allowed for this metric.</param>
public sealed record MetricRegistration(
    string Name,
    string Unit,
    string AggKind,
    string Description,
    string[] AllowedTags);
