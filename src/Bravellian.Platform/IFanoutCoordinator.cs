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
/// Coordinates the fanout process by acquiring a lease, running the planner, and dispatching slices.
/// This is the main orchestration component that ties together all fanout operations.
/// </summary>
public interface IFanoutCoordinator
{
    /// <summary>
    /// Runs a fanout planning pass under a distributed lease and dispatches the resulting slices.
    /// Only one coordinator instance will be active for a given fanout topic/work key combination.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic to coordinate.</param>
    /// <param name="workKey">Optional work key to filter planning (null means all work keys).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of slices that were dispatched.</returns>
    Task<int> RunAsync(string fanoutTopic, string? workKey, CancellationToken ct);
}