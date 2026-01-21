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

using System.ComponentModel.DataAnnotations;

namespace Bravellian.Platform;

public class SqlSchedulerOptions
{
    public const string SectionName = "SqlScheduler";

    /// <summary>
    /// Gets or sets the database connection string for the scheduler.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum time the scheduler will sleep before re-checking for new jobs,
    /// even if the next scheduled job is far in the future.
    /// Recommended: 30 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:15:00")]
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether if true, the background IHostedService workers (SqlSchedulerService, OutboxProcessor)
    /// will be registered and started. Set to false for environments where you only
    /// want to schedule jobs (e.g., in a web API) but not execute them.
    /// </summary>
    public bool EnableBackgroundWorkers { get; set; } = true;

    /// <summary>
    /// Gets or sets the database schema name for all scheduler tables.
    /// Defaults to "dbo".
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the table name for jobs.
    /// Defaults to "Jobs".
    /// </summary>
    public string JobsTableName { get; set; } = "Jobs";

    /// <summary>
    /// Gets or sets the table name for job runs.
    /// Defaults to "JobRuns".
    /// </summary>
    public string JobRunsTableName { get; set; } = "JobRuns";

    /// <summary>
    /// Gets or sets the table name for timers.
    /// Defaults to "Timers".
    /// </summary>
    public string TimersTableName { get; set; } = "Timers";

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated on startup.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}
