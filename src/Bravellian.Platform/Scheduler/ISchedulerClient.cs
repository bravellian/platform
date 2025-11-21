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
/// A client for scheduling and managing durable timers and recurring jobs,
/// with support for claiming and processing scheduled work items.
/// </summary>
public interface ISchedulerClient
{
    /// <summary>
    /// Schedules a one-time timer to be executed at a specific time.
    /// </summary>
    /// <param name="topic">The topic that identifies the work to be done.</param>
    /// <param name="payload">The data required for the work.</param>
    /// <param name="dueTime">The UTC time when the timer should fire.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A unique ID for the scheduled timer.</returns>
    Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a pending timer.
    /// </summary>
    /// <param name="timerId">The ID of the timer to cancel.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if a pending timer was found and cancelled; otherwise, false.</returns>
    Task<bool> CancelTimerAsync(string timerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a recurring job definition.
    /// </summary>
    /// <param name="jobName">A unique name for the job.</param>
    /// <param name="topic">The topic that identifies the work to be done.</param>
    /// <param name="cronSchedule">The CRON expression for the schedule (e.g., "0 */5 * * * *").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a recurring job definition.
    /// </summary>
    /// <param name="jobName">A unique name for the job.</param>
    /// <param name="topic">The topic that identifies the work to be done.</param>
    /// <param name="cronSchedule">The CRON expression for the schedule (e.g., "0 */5 * * * *").</param>
    /// <param name="payload">The data required for the work.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a recurring job definition and all its pending runs.
    /// </summary>
    /// <param name="jobName">The unique name of the job to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteJobAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a job to run immediately, outside of its normal schedule.
    /// </summary>
    /// <param name="jobName">The unique name of the job to trigger.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task TriggerJobAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>
    /// Claims ready timer items atomically with a lease for processing.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the claiming process.</param>
    /// <param name="leaseSeconds">The duration in seconds to hold the lease.</param>
    /// <param name="batchSize">The maximum number of items to claim.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of claimed timer identifiers.</returns>
    Task<IReadOnlyList<Guid>> ClaimTimersAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims ready job run items atomically with a lease for processing.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the claiming process.</param>
    /// <param name="leaseSeconds">The duration in seconds to hold the lease.</param>
    /// <param name="batchSize">The maximum number of items to claim.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of claimed job run identifiers.</returns>
    Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges timer items as successfully processed.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of timers to acknowledge.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AckTimersAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acknowledges job run items as successfully processed.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of job runs to acknowledge.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AckJobRunsAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abandons timer items, returning them to the ready state for retry.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of timers to abandon.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AbandonTimersAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abandons job run items, returning them to the ready state for retry.
    /// </summary>
    /// <param name="ownerToken">The unique token identifying the owning process.</param>
    /// <param name="ids">The identifiers of job runs to abandon.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AbandonJobRunsAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reaps expired timer items, returning them to ready state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReapExpiredTimersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reaps expired job run items, returning them to ready state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken);
}
