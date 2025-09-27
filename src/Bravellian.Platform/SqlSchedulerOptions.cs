using System.ComponentModel.DataAnnotations;

namespace Bravellian.Platform;

public class SqlSchedulerOptions
{
    public const string SectionName = "SqlScheduler";

    /// <summary>
    /// The database connection string for the scheduler.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The maximum time the scheduler will sleep before re-checking for new jobs,
    /// even if the next scheduled job is far in the future.
    /// Recommended: 30 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:15:00")]
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// If true, the background IHostedService workers (SqlSchedulerService, OutboxProcessor)
    /// will be registered and started. Set to false for environments where you only
    /// want to schedule jobs (e.g., in a web API) but not execute them.
    /// </summary>
    public bool EnableBackgroundWorkers { get; set; } = true;
}