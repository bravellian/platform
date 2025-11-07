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
/// Repository for managing fanout policies that define cadence and jitter settings.
/// These policies determine how frequently each fanout topic/work key combination should run.
/// </summary>
public interface IFanoutPolicyRepository
{
    /// <summary>
    /// Gets the cadence configuration for a specific fanout topic and work key.
    /// Returns default values if no specific policy is configured.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic name.</param>
    /// <param name="workKey">The work key within the topic.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A tuple containing the cadence in seconds and jitter in seconds.</returns>
    Task<(int everySeconds, int jitterSeconds)> GetCadenceAsync(string fanoutTopic, string workKey, CancellationToken ct);

    /// <summary>
    /// Sets or updates the cadence policy for a fanout topic and work key.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic name.</param>
    /// <param name="workKey">The work key within the topic.</param>
    /// <param name="everySeconds">How often the fanout should run in seconds.</param>
    /// <param name="jitterSeconds">Random jitter to add to prevent thundering herd.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task SetCadenceAsync(string fanoutTopic, string workKey, int everySeconds, int jitterSeconds, CancellationToken ct);
}
