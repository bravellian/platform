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

namespace Bravellian.Platform.Observability;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines a sink for processing heartbeat events.
/// </summary>
public interface IHeartbeatSink
{
    /// <summary>
    /// Invoked when a heartbeat occurs.
    /// </summary>
    /// <param name="context">The heartbeat context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnHeartbeatAsync(HeartbeatContext context, CancellationToken cancellationToken);
}
