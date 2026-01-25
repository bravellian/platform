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

public class PostgresInboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database schema name for the inbox table.
    /// Defaults to "infra".
    /// </summary>
    public string SchemaName { get; set; } = "infra";

    /// <summary>
    /// Gets or sets the table name for the inbox.
    /// Defaults to "Inbox".
    /// </summary>
    public string TableName { get; set; } = "Inbox";

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated on startup.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed messages.
    /// Defaults to 5.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the lease duration in seconds for claimed messages.
    /// Defaults to 30 seconds.
    /// </summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the retention period for processed inbox messages.
    /// Messages older than this period will be deleted during cleanup.
    /// Defaults to 7 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets a value indicating whether automatic cleanup of old processed messages is enabled.
    /// When true, a background service will periodically delete processed messages older than RetentionPeriod.
    /// Defaults to true.
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval at which the cleanup job runs.
    /// Defaults to 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}





