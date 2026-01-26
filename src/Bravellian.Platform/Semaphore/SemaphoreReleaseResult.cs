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

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// Result of a Release operation.
/// </summary>
[SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Result type does not require custom equality semantics.")]
public readonly struct SemaphoreReleaseResult
{
    /// <summary>
    /// Gets the status of the release operation.
    /// </summary>
    public required SemaphoreReleaseStatus Status { get; init; }

    /// <summary>
    /// Creates a Released result.
    /// </summary>
    public static SemaphoreReleaseResult Released()
    {
        return new SemaphoreReleaseResult
        {
            Status = SemaphoreReleaseStatus.Released,
        };
    }

    /// <summary>
    /// Creates a NotFound result.
    /// </summary>
    public static SemaphoreReleaseResult NotFound()
    {
        return new SemaphoreReleaseResult
        {
            Status = SemaphoreReleaseStatus.NotFound,
        };
    }

    /// <summary>
    /// Creates an Unavailable result.
    /// </summary>
    public static SemaphoreReleaseResult Unavailable()
    {
        return new SemaphoreReleaseResult
        {
            Status = SemaphoreReleaseStatus.Unavailable,
        };
    }
}
