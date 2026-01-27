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

namespace Bravellian.Platform.ExactlyOnce;

/// <summary>
/// Probes the system to verify whether the side effect already occurred.
/// </summary>
/// <typeparam name="TItem">Item type.</typeparam>
public interface IExactlyOnceProbe<in TItem>
{
    /// <summary>
    /// Attempts to confirm whether the side effect already occurred.
    /// </summary>
    /// <param name="item">Item being processed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Probe result.</returns>
    Task<ExactlyOnceProbeResult> ProbeAsync(TItem item, CancellationToken cancellationToken);
}
