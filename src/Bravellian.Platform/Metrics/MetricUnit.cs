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
/// Standard metric units.
/// </summary>
public static class MetricUnit
{
    /// <summary>
    /// Dimensionless count.
    /// </summary>
    public const string Count = "count";

    /// <summary>
    /// Milliseconds.
    /// </summary>
    public const string Milliseconds = "ms";

    /// <summary>
    /// Seconds.
    /// </summary>
    public const string Seconds = "seconds";

    /// <summary>
    /// Bytes.
    /// </summary>
    public const string Bytes = "bytes";

    /// <summary>
    /// Percentage (0-100).
    /// </summary>
    public const string Percent = "percent";
}
