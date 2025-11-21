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
/// <summary>
/// Provides state information about the scheduler for monitoring.
/// </summary>
public interface ISchedulerState
{
    /// <summary>
    /// Gets a list of overdue jobs.
    /// </summary>
    /// <param name="threshold">The threshold for determining if a job is overdue.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of overdue job identifiers with their due times.</returns>
    Task<IReadOnlyList<(string JobId, DateTimeOffset DueTime)>> GetOverdueJobsAsync(TimeSpan threshold, CancellationToken cancellationToken);
}
