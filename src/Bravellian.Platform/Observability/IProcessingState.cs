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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides state information about processors for monitoring.
/// </summary>
public interface IProcessingState
{
    /// <summary>
    /// Gets a list of idle or non-running processors.
    /// </summary>
    /// <param name="threshold">The threshold for determining if a processor is idle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of processor identifiers with their last activity times.</returns>
    Task<IReadOnlyList<(string ProcessorId, string Component, DateTimeOffset LastActivityAt)>> GetIdleProcessorsAsync(TimeSpan threshold, CancellationToken cancellationToken);
}
