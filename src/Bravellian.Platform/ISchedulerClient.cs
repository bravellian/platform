using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bravellian.Platform;
using System;
using System.Threading.Tasks;

/// <summary>
/// A client for scheduling and managing durable timers and recurring jobs.
/// </summary>
public interface ISchedulerClient
{
    /// <summary>
    /// Schedules a one-time timer to be executed at a specific time.
    /// </summary>
    /// <param name="topic">The topic that identifies the work to be done.</param>
    /// <param name="payload">The data required for the work.</param>
    /// <param name="dueTime">The UTC time when the timer should fire.</param>
    /// <returns>A unique ID for the scheduled timer.</returns>
    Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime);

    /// <summary>
    /// Cancels a pending timer.
    /// </summary>
    /// <param name="timerId">The ID of the timer to cancel.</param>
    /// <returns>True if a pending timer was found and cancelled; otherwise, false.</returns>
    Task<bool> CancelTimerAsync(string timerId);

    /// <summary>
    /// Creates or updates a recurring job definition.
    /// </summary>
    /// <param name="jobName">A unique name for the job.</param>
    /// <param name="topic">The topic that identifies the work to be done.</param>
    /// <param name="cronSchedule">The CRON expression for the schedule (e.g., "0 */5 * * * *").</param>
    /// <param name="payload">The data required for the work.</param>
    Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload = null);

    /// <summary>
    /// Deletes a recurring job definition and all its pending runs.
    /// </summary>
    /// <param name="jobName">The unique name of the job to delete.</param>
    Task DeleteJobAsync(string jobName);

    /// <summary>
    /// Triggers a job to run immediately, outside of its normal schedule.
    /// </summary>
    /// <param name="jobName">The unique name of the job to trigger.</param>
    Task TriggerJobAsync(string jobName);
}
