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
/// Represents a snapshot of aggregated metric values.
/// </summary>
internal sealed class MetricSnapshot
{
    public double Sum { get; init; }

    public int Count { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Last { get; init; }

    public double? P50 { get; init; }

    public double? P95 { get; init; }

    public double? P99 { get; init; }
}
