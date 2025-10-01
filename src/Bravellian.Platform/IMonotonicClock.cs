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

namespace Bravellian.Platform;

/// <summary>
/// Provides monotonic time measurements for durations and timeouts.
/// Monotonic time is not affected by system clock adjustments and should be used
/// for measuring elapsed time, timeouts, and relative timing.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>
    /// Gets the current monotonic time in high-resolution ticks.
    /// </summary>
    long Ticks { get; }

    /// <summary>
    /// Gets the current monotonic time in seconds.
    /// </summary>
    double Seconds { get; }
}