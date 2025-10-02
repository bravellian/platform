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
/// Handles the dispatching of fanout slices to the underlying messaging system.
/// Default implementation uses Outbox, but this interface allows for custom dispatch strategies.
/// </summary>
public interface IFanoutDispatcher
{
    /// <summary>
    /// Enqueues slices to the messaging system (typically Outbox) for processing.
    /// Each slice becomes one message with topic following the pattern "fanout:{fanoutTopic}:{workKey}".
    /// </summary>
    /// <param name="slices">The slices to dispatch for processing.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The number of slices successfully dispatched.</returns>
    Task<int> DispatchAsync(IEnumerable<FanoutSlice> slices, CancellationToken ct);
}