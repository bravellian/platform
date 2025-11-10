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

using System;
using System.Collections.Generic;

/// <summary>
/// Represents a unique key for a metric time series.
/// </summary>
internal sealed record MetricSeriesKey
{
    public required string MetricName { get; init; }

    public required Guid? DatabaseId { get; init; }

    public required string Service { get; init; }

    public required Guid InstanceId { get; init; }

    public required IReadOnlyDictionary<string, string> Tags { get; init; }
}
