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

using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

/// <summary>
/// Manages database schema creation and verification for the Platform components.
/// </summary>
internal static class DatabaseSchemaManager
{
    /// <summary>
    /// Ensures that the required database schema exists for the outbox functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="tableName">The table name (default: "Outbox").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureOutboxSchemaAsync(string connectionString, string schemaName = "dbo", string tableName = "Outbox")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Check if table exists
        var tableExists = await TableExistsAsync(connection, schemaName, tableName).ConfigureAwait(false);
        if (!tableExists)
        {
            var createScript = GetOutboxCreateScript(schemaName, tableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that the required database schema exists for the scheduler functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="jobsTableName">The jobs table name (default: "Jobs").</param>
    /// <param name="jobRunsTableName">The job runs table name (default: "JobRuns").</param>
    /// <param name="timersTableName">The timers table name (default: "Timers").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureSchedulerSchemaAsync(string connectionString, string schemaName = "dbo", string jobsTableName = "Jobs", string jobRunsTableName = "JobRuns", string timersTableName = "Timers")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Create Jobs table first (referenced by JobRuns)
        var jobsExists = await TableExistsAsync(connection, schemaName, jobsTableName).ConfigureAwait(false);
        if (!jobsExists)
        {
            var createScript = GetJobsCreateScript(schemaName, jobsTableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create Timers table
        var timersExists = await TableExistsAsync(connection, schemaName, timersTableName).ConfigureAwait(false);
        if (!timersExists)
        {
            var createScript = GetTimersCreateScript(schemaName, timersTableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create JobRuns table (has FK to Jobs)
        var jobRunsExists = await TableExistsAsync(connection, schemaName, jobRunsTableName).ConfigureAwait(false);
        if (!jobRunsExists)
        {
            var createScript = GetJobRunsCreateScript(schemaName, jobRunsTableName, jobsTableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks if a table exists in the specified schema.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>True if the table exists, false otherwise.</returns>
    private static async Task<bool> TableExistsAsync(SqlConnection connection, string schemaName, string tableName)
    {
        const string sql = @"
            SELECT COUNT(1) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName";

        var count = await connection.QuerySingleAsync<int>(sql, new { SchemaName = schemaName, TableName = tableName }).ConfigureAwait(false);
        return count > 0;
    }

    /// <summary>
    /// Executes a SQL script, handling GO statements.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="script">The SQL script to execute.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ExecuteScriptAsync(SqlConnection connection, string script)
    {
        // Split by GO statements and execute each batch separately
        var batches = script.Split(new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "GO" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var batch in batches)
        {
            var trimmedBatch = batch.Trim();
            if (!string.IsNullOrEmpty(trimmedBatch))
            {
                await connection.ExecuteAsync(trimmedBatch).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gets the SQL script to create the Outbox table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetOutboxCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    -- Core Fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- Processing Status & Auditing
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL, -- e.g., machine name or instance ID

    -- For Robustness & Error Handling
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(), -- For backoff strategies

    -- For Idempotency & Tracing
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- A stable ID for the message consumer
    CorrelationId UNIQUEIDENTIFIER NULL -- To trace a message through multiple systems
);

-- An index to efficiently query for unprocessed messages, now including the next attempt time.
CREATE INDEX IX_{tableName}_GetNext ON [{schemaName}].[{tableName}](IsProcessed, NextAttemptAt)
    INCLUDE(Id, Payload, Topic, RetryCount) -- Include columns needed for processing
    WHERE IsProcessed = 0;";
    }

    /// <summary>
    /// Gets the SQL script to create the Jobs table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetJobsCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobName NVARCHAR(100) NOT NULL,
    CronSchedule NVARCHAR(100) NOT NULL, -- e.g., ""0 */5 * * * *"" for every 5 minutes
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,

    -- State tracking for the scheduler
    NextDueTime DATETIMEOFFSET NULL,
    LastRunTime DATETIMEOFFSET NULL,
    LastRunStatus NVARCHAR(20) NULL
);

-- Unique index to prevent duplicate job definitions
CREATE UNIQUE INDEX UQ_{tableName}_JobName ON [{schemaName}].[{tableName}](JobName);";
    }

    /// <summary>
    /// Gets the SQL script to create the Timers table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetTimersCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    -- Core Fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    DueTime DATETIMEOFFSET NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,

    -- For tracing back to business logic
    CorrelationId NVARCHAR(255) NULL,

    -- Processing State Management
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending, Claimed, Processed, Failed
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,

    -- Auditing
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ProcessedAt DATETIMEOFFSET NULL,
    LastError NVARCHAR(MAX) NULL
);

-- A critical index to find the next due timers efficiently.
CREATE INDEX IX_{tableName}_GetNext ON [{schemaName}].[{tableName}](Status, DueTime)
    INCLUDE(Id, Topic) -- Include columns needed to start processing
    WHERE Status = 'Pending';";
    }

    /// <summary>
    /// Gets the SQL script to create the JobRuns table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="jobsTableName">The jobs table name for foreign key reference.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetJobRunsCreateScript(string schemaName, string tableName, string jobsTableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [{schemaName}].[{jobsTableName}](Id),
    ScheduledTime DATETIMEOFFSET NOT NULL,

    -- Processing State Management
    Status NVARCHAR(20) NOT NULL DEFAULT 'Pending', -- Pending, Claimed, Running, Succeeded, Failed
    ClaimedBy NVARCHAR(100) NULL,
    ClaimedAt DATETIMEOFFSET NULL,
    RetryCount INT NOT NULL DEFAULT 0,

    -- Auditing and Results
    StartTime DATETIMEOFFSET NULL,
    EndTime DATETIMEOFFSET NULL,
    Output NVARCHAR(MAX) NULL,
    LastError NVARCHAR(MAX) NULL
);

-- Index to find pending job runs that are due
CREATE INDEX IX_{tableName}_GetNext ON [{schemaName}].[{tableName}](Status, ScheduledTime)
    WHERE Status = 'Pending';";
    }
}
}