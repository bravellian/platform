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
/// Represents a monotonic deadline that can be used to check if a certain point in time has been reached.
/// This is useful for timeouts and scheduling that should not be affected by system clock adjustments.
/// </summary>
/// <param name="atSeconds">The target time in monotonic seconds.</param>
public readonly record struct MonoDeadline(double atSeconds)
{
    /// <summary>
    /// Checks if this deadline has expired based on the current monotonic clock time.
    /// </summary>
    /// <param name="clock">The monotonic clock to check against.</param>
    /// <returns>True if the deadline has expired; otherwise, false.</returns>
    public bool Expired(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return clock.Seconds >= atSeconds;
    }

    /// <summary>
    /// Creates a new deadline that will expire after the specified time span from the current monotonic time.
    /// </summary>
    /// <param name="clock">The monotonic clock to use for timing.</param>
    /// <param name="span">The time span from now when the deadline should expire.</param>
    /// <returns>A new MonoDeadline that will expire at the specified time.</returns>
    public static MonoDeadline In(IMonotonicClock clock, TimeSpan span)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new MonoDeadline(clock.Seconds + span.TotalSeconds);
    }
}
