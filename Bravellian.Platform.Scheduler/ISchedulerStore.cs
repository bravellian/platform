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
/// Represents scheduler operations for a specific database instance.
/// This abstraction enables the scheduler to work with multiple databases.
/// </summary>
public interface ISchedulerStore
{
    /// <summary>
    /// Gets the next event time (minimum of next timer, job run, or job due time).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next event time, or null if no work is scheduled.</returns>
    Task<DateTimeOffset?> GetNextEventTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates job runs from due jobs and returns the count of created runs.
    /// </summary>
    /// <param name="lease">The system lease for fencing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of job runs created.</returns>
    Task<int> CreateJobRunsFromDueJobsAsync(ISystemLease lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims and returns due timers that are ready to be dispatched.
    /// </summary>
    /// <param name="lease">The system lease for fencing.</param>
    /// <param name="batchSize">Maximum number of timers to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of claimed timers.</returns>
    Task<IReadOnlyList<(Guid Id, string Topic, string Payload)>> ClaimDueTimersAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims and returns due job runs that are ready to be dispatched.
    /// </summary>
    /// <param name="lease">The system lease for fencing.</param>
    /// <param name="batchSize">Maximum number of job runs to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of claimed job runs.</returns>
    Task<IReadOnlyList<(Guid Id, Guid JobId, string Topic, string Payload)>> ClaimDueJobRunsAsync(
        ISystemLease lease,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the scheduler state with the current fencing token.
    /// </summary>
    /// <param name="lease">The system lease for fencing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateSchedulerStateAsync(ISystemLease lease, CancellationToken cancellationToken = default);
}
