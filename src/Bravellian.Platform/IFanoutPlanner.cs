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
/// Implemented by application code to decide which slices are due for processing now.
/// This interface provides the domain-specific logic for determining when work needs to be scheduled.
/// </summary>
public interface IFanoutPlanner
{
    /// <summary>
    /// Returns the slices that are due now for this topic (optionally filtered by WorkKey).
    /// Implementations should check cadence, last completion times, and any business-specific
    /// criteria to determine which work units should be processed.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic to plan for.</param>
    /// <param name="workKey">Optional filter to restrict planning to a specific work key.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A list of slices that are ready for processing.</returns>
    Task<IReadOnlyList<FanoutSlice>> GetDueSlicesAsync(
        string fanoutTopic,
        string? workKey,
        CancellationToken ct);
}
