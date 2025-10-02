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

        // Check if OutboxState table exists for fencing token management
        var stateTableExists = await TableExistsAsync(connection, schemaName, "OutboxState").ConfigureAwait(false);
        if (!stateTableExists)
        {
            var createStateScript = GetOutboxStateCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createStateScript).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that the required database schema exists for the distributed lock functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="tableName">The table name (default: "DistributedLock").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureDistributedLockSchemaAsync(string connectionString, string schemaName = "dbo", string tableName = "DistributedLock")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Check if table exists
        var tableExists = await TableExistsAsync(connection, schemaName, tableName).ConfigureAwait(false);
        if (!tableExists)
        {
            var createScript = GetDistributedLockCreateScript(schemaName, tableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureDistributedLockStoredProceduresAsync(connection, schemaName).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures that the required database schema exists for the lease functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="tableName">The table name (default: "Lease").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureLeaseSchemaAsync(string connectionString, string schemaName = "dbo", string tableName = "Lease")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Check if table exists
        var tableExists = await TableExistsAsync(connection, schemaName, tableName).ConfigureAwait(false);
        if (!tableExists)
        {
            var createScript = GetLeaseCreateScript(schemaName, tableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureLeaseStoredProceduresAsync(connection, schemaName).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures that the required database schema exists for the inbox functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="tableName">The table name (default: "Inbox").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureInboxSchemaAsync(string connectionString, string schemaName = "dbo", string tableName = "Inbox")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Check if table exists
        var tableExists = await TableExistsAsync(connection, schemaName, tableName).ConfigureAwait(false);
        if (!tableExists)
        {
            var createScript = GetInboxCreateScript(schemaName, tableName);
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

        // Check if SchedulerState table exists for fencing token management
        var schedulerStateExists = await TableExistsAsync(connection, schemaName, "SchedulerState").ConfigureAwait(false);
        if (!schedulerStateExists)
        {
            var createStateScript = GetSchedulerStateCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createStateScript).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that the required database schema exists for the fanout functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <param name="policyTableName">The policy table name (default: "FanoutPolicy").</param>
    /// <param name="cursorTableName">The cursor table name (default: "FanoutCursor").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureFanoutSchemaAsync(string connectionString, string schemaName = "dbo", string policyTableName = "FanoutPolicy", string cursorTableName = "FanoutCursor")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Create FanoutPolicy table first (no dependencies)
        var policyExists = await TableExistsAsync(connection, schemaName, policyTableName).ConfigureAwait(false);
        if (!policyExists)
        {
            var createScript = GetFanoutPolicyCreateScript(schemaName, policyTableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create FanoutCursor table
        var cursorExists = await TableExistsAsync(connection, schemaName, cursorTableName).ConfigureAwait(false);
        if (!cursorExists)
        {
            var createScript = GetFanoutCursorCreateScript(schemaName, cursorTableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that a schema exists in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureSchemaExistsAsync(SqlConnection connection, string schemaName)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @SchemaName)
            BEGIN
                EXEC('CREATE SCHEMA [' + @SchemaName + ']')
            END";

        await connection.ExecuteAsync(sql, new { SchemaName = schemaName }).ConfigureAwait(false);
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
    CorrelationId NVARCHAR(255) NULL -- To trace a message through multiple systems
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

    /// <summary>
    /// Gets the SQL script to create the DistributedLock table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetDistributedLockCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}](
    [ResourceName] SYSNAME NOT NULL CONSTRAINT PK_{tableName} PRIMARY KEY,
    [OwnerToken] UNIQUEIDENTIFIER NULL,
    [LeaseUntil] DATETIME2(3) NULL,
    [FencingToken] BIGINT NOT NULL CONSTRAINT DF_{tableName}_Fence DEFAULT(0),
    [ContextJson] NVARCHAR(MAX) NULL,
    [Version] ROWVERSION NOT NULL
);

CREATE INDEX IX_{tableName}_OwnerToken ON [{schemaName}].[{tableName}]([OwnerToken])
    WHERE [OwnerToken] IS NOT NULL;";
    }

    /// <summary>
    /// Gets the SQL script to create the Lease table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetLeaseCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}](
    [Name] SYSNAME NOT NULL CONSTRAINT PK_{tableName} PRIMARY KEY,
    [Owner] SYSNAME NULL,
    [LeaseUntilUtc] DATETIME2(3) NULL,
    [LastGrantedUtc] DATETIME2(3) NULL,
    [Version] ROWVERSION NOT NULL
);";
    }

    /// <summary>
    /// Ensures distributed lock stored procedures exist.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureDistributedLockStoredProceduresAsync(SqlConnection connection, string schemaName)
    {
        var acquireProc = GetLockAcquireStoredProcedure(schemaName);
        var renewProc = GetLockRenewStoredProcedure(schemaName);
        var releaseProc = GetLockReleaseStoredProcedure(schemaName);
        var cleanupProc = GetLockCleanupStoredProcedure(schemaName);

        await ExecuteScriptAsync(connection, acquireProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, renewProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, releaseProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, cleanupProc).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures lease stored procedures exist.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureLeaseStoredProceduresAsync(SqlConnection connection, string schemaName)
    {
        var acquireProc = GetLeaseAcquireStoredProcedure(schemaName);
        var renewProc = GetLeaseRenewStoredProcedure(schemaName);

        await ExecuteScriptAsync(connection, acquireProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, renewProc).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the Lock_Acquire stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockAcquireStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_Acquire]
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @ContextJson NVARCHAR(MAX) = NULL,
    @UseGate BIT = 0,
    @GateTimeoutMs INT = 200,
    @Acquired BIT OUTPUT,
    @FencingToken BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);
    DECLARE @rc INT;
    DECLARE @LockResourceName NVARCHAR(255) = CONCAT('lease:', @ResourceName);

    -- Optional micro critical section to serialize row upsert under high contention
    IF (@UseGate = 1)
    BEGIN
        EXEC @rc = sp_getapplock
            @Resource    = @LockResourceName,
            @LockMode    = 'Exclusive',
            @LockOwner   = 'Session',
            @LockTimeout = @GateTimeoutMs,
            @DbPrincipal = 'public';
        IF (@rc < 0)
        BEGIN
            SET @Acquired = 0; SET @FencingToken = NULL;
            RETURN;
        END
    END

    BEGIN TRAN;

    -- Ensure row exists, holding a key-range lock to avoid races on insert
    IF NOT EXISTS (SELECT 1 FROM [{schemaName}].[DistributedLock] WITH (UPDLOCK, HOLDLOCK)
                   WHERE ResourceName = @ResourceName)
    BEGIN
        INSERT [{schemaName}].[DistributedLock] (ResourceName, OwnerToken, LeaseUntil, ContextJson)
        VALUES (@ResourceName, NULL, NULL, NULL);
    END

    -- Take or re-take the lease (re-entrant allowed)
    UPDATE dl WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken =
             CASE WHEN dl.OwnerToken = @OwnerToken THEN dl.OwnerToken ELSE @OwnerToken END,
           LeaseUntil = @newLease,
           ContextJson = @ContextJson,
           FencingToken =
             CASE WHEN dl.OwnerToken = @OwnerToken
                  THEN dl.FencingToken + 1         -- re-entrant renew-on-acquire bumps too
                  ELSE dl.FencingToken + 1         -- new owner bumps
             END
      FROM [{schemaName}].[DistributedLock] dl
     WHERE dl.ResourceName = @ResourceName
       AND (dl.OwnerToken IS NULL OR dl.LeaseUntil IS NULL OR dl.LeaseUntil <= @now OR dl.OwnerToken = @OwnerToken);

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [{schemaName}].[DistributedLock]
         WHERE ResourceName = @ResourceName;
        SET @Acquired = 1;
    END
    ELSE
    BEGIN
        SET @Acquired = 0; SET @FencingToken = NULL;
    END

    COMMIT TRAN;

    IF (@UseGate = 1)
        EXEC sp_releaseapplock
             @Resource  = @LockResourceName,
             @LockOwner = 'Session';
END";
    }

    /// <summary>
    /// Gets the Lock_Renew stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockRenewStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_Renew]
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @Renewed BIT OUTPUT,
    @FencingToken BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    UPDATE dl WITH (UPDLOCK, ROWLOCK)
       SET LeaseUntil = @newLease,
           FencingToken = dl.FencingToken + 1
      FROM [{schemaName}].[DistributedLock] dl
     WHERE dl.ResourceName = @ResourceName
       AND dl.OwnerToken   = @OwnerToken
       AND dl.LeaseUntil   > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [{schemaName}].[DistributedLock]
         WHERE ResourceName = @ResourceName;
        SET @Renewed = 1;
    END
    ELSE
    BEGIN
        SET @Renewed = 0; SET @FencingToken = NULL;
    END
END";
    }

    /// <summary>
    /// Gets the Lock_Release stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockReleaseStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_Release]
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [{schemaName}].[DistributedLock] WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken = NULL,
           LeaseUntil = NULL,
           ContextJson = NULL
     WHERE ResourceName = @ResourceName
       AND OwnerToken   = @OwnerToken;
END";
    }

    /// <summary>
    /// Gets the Lock_CleanupExpired stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockCleanupStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_CleanupExpired]
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [{schemaName}].[DistributedLock]
       SET OwnerToken = NULL, LeaseUntil = NULL, ContextJson = NULL
     WHERE LeaseUntil IS NOT NULL AND LeaseUntil <= SYSUTCDATETIME();
END";
    }

    /// <summary>
    /// Gets the Lease_Acquire stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLeaseAcquireStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lease_Acquire]
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Acquired BIT OUTPUT,
    @ServerUtcNow DATETIME2(3) OUTPUT,
    @LeaseUntilUtc DATETIME2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);
    
    SET @ServerUtcNow = @now;
    SET @Acquired = 0;
    SET @LeaseUntilUtc = NULL;

    BEGIN TRAN;

    -- Ensure row exists atomically
    MERGE [{schemaName}].[Lease] AS target
    USING (SELECT @Name AS Name) AS source
    ON (target.Name = source.Name)
    WHEN NOT MATCHED THEN
        INSERT (Name, Owner, LeaseUntilUtc, LastGrantedUtc)
        VALUES (source.Name, NULL, NULL, NULL);

    -- Try to acquire lease if free or expired
    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET Owner = @Owner,
           LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [{schemaName}].[Lease] l
     WHERE l.Name = @Name
       AND (l.Owner IS NULL OR l.LeaseUntilUtc IS NULL OR l.LeaseUntilUtc <= @now);

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Acquired = 1;
        SET @LeaseUntilUtc = @newLease;
    END

    COMMIT TRAN;
END";
    }

    /// <summary>
    /// Gets the Lease_Renew stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLeaseRenewStoredProcedure(string schemaName)
    {
        return $@"
CREATE OR ALTER PROCEDURE [{schemaName}].[Lease_Renew]
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Renewed BIT OUTPUT,
    @ServerUtcNow DATETIME2(3) OUTPUT,
    @LeaseUntilUtc DATETIME2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);
    
    SET @ServerUtcNow = @now;
    SET @Renewed = 0;
    SET @LeaseUntilUtc = NULL;

    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [{schemaName}].[Lease] l
     WHERE l.Name = @Name
       AND l.Owner = @Owner
       AND l.LeaseUntilUtc > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Renewed = 1;
        SET @LeaseUntilUtc = @newLease;
    END
END";
    }

    /// <summary>
    /// Gets the SQL script to create the Inbox table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetInboxCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    -- Core identification
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,
    
    -- Timing tracking
    FirstSeenUtc DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    LastSeenUtc DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    ProcessedUtc DATETIME2(3) NULL,
    
    -- Processing status
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'
        CONSTRAINT CK_{tableName}_Status CHECK (Status IN ('Seen', 'Processing', 'Done', 'Dead'))
);

-- Index for querying processed messages efficiently
CREATE INDEX IX_{tableName}_ProcessedUtc ON [{schemaName}].[{tableName}](ProcessedUtc)
    WHERE ProcessedUtc IS NOT NULL;

-- Index for querying by status
CREATE INDEX IX_{tableName}_Status ON [{schemaName}].[{tableName}](Status);

-- Index for efficient cleanup of old processed messages
CREATE INDEX IX_{tableName}_Status_ProcessedUtc ON [{schemaName}].[{tableName}](Status, ProcessedUtc)
    WHERE Status = 'Done' AND ProcessedUtc IS NOT NULL;";
    }

    /// <summary>
    /// Gets the SQL script to create the OutboxState table for fencing token management.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetOutboxStateCreateScript(string schemaName)
    {
        return $@"
CREATE TABLE [{schemaName}].[OutboxState] (
    Id INT NOT NULL CONSTRAINT PK_OutboxState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastDispatchAt DATETIME2(3) NULL
);

-- Insert initial state row
INSERT [{schemaName}].[OutboxState] (Id, CurrentFencingToken, LastDispatchAt) 
VALUES (1, 0, NULL);";
    }

    /// <summary>
    /// Gets the SQL script to create the SchedulerState table for fencing token management.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetSchedulerStateCreateScript(string schemaName)
    {
        return $@"
CREATE TABLE [{schemaName}].[SchedulerState] (
    Id INT NOT NULL CONSTRAINT PK_SchedulerState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastRunAt DATETIME2(3) NULL
);

-- Insert initial state row
INSERT [{schemaName}].[SchedulerState] (Id, CurrentFencingToken, LastRunAt) 
VALUES (1, 0, NULL);";
    }

    /// <summary>
    /// Ensures that the work queue pattern columns and stored procedures exist for all platform tables.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureWorkQueueSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Apply work queue migration (columns and types)
            var migrationScript = GetWorkQueueMigrationInlineScript();
            await ExecuteScriptAsync(connection, migrationScript).ConfigureAwait(false);

            // Create each stored procedure individually to avoid batch issues
            await CreateOutboxProceduresAsync(connection).ConfigureAwait(false);
        }
        catch (SqlException sqlEx)
        {
            throw new InvalidOperationException($"Failed to ensure work queue schema. SQL Error: {sqlEx.Message} (Error Number: {sqlEx.Number}, Severity: {sqlEx.Class}, State: {sqlEx.State})", sqlEx);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to ensure work queue schema: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates the Outbox work queue stored procedures individually.
    /// </summary>
    /// <param name="connection">The SQL connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CreateOutboxProceduresAsync(SqlConnection connection)
    {
        // Create procedures one by one to avoid batch execution issues
        var procedures = new[]
        {
            @"CREATE OR ALTER PROCEDURE dbo.Outbox_Claim
                @OwnerToken UNIQUEIDENTIFIER,
                @LeaseSeconds INT,
                @BatchSize INT = 50
              AS
              BEGIN
                SET NOCOUNT ON;
                DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
                DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                WITH cte AS (
                    SELECT TOP (@BatchSize) Id
                    FROM dbo.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE Status = 0 AND (LockedUntil IS NULL OR LockedUntil <= @now)
                    ORDER BY CreatedAt
                )
                UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
                OUTPUT inserted.Id
                FROM dbo.Outbox o JOIN cte ON cte.Id = o.Id;
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Outbox_Ack
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.GuidIdList READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
                FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Outbox_Abandon
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.GuidIdList READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
                FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Outbox_Fail
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.GuidIdList READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET Status = 3, OwnerToken = NULL, LockedUntil = NULL
                FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Outbox_ReapExpired
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE dbo.Outbox SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
                WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
                SELECT @@ROWCOUNT AS ReapedCount;
              END",
        };

        foreach (var procedure in procedures)
        {
            await connection.ExecuteAsync(procedure).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the Inbox work queue stored procedures individually.
    /// </summary>
    /// <param name="connection">The SQL connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CreateInboxProceduresAsync(SqlConnection connection)
    {
        // Create procedures one by one to avoid batch execution issues
        var procedures = new[]
        {
            @"CREATE OR ALTER PROCEDURE dbo.Inbox_Claim
                @OwnerToken UNIQUEIDENTIFIER,
                @LeaseSeconds INT,
                @BatchSize INT = 50
              AS
              BEGIN
                SET NOCOUNT ON;
                DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
                DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                WITH cte AS (
                    SELECT TOP (@BatchSize) MessageId
                    FROM dbo.Inbox WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE Status IN ('Seen', 'Processing') AND (LockedUntil IS NULL OR LockedUntil <= @now)
                    ORDER BY LastSeenUtc
                )
                UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
                OUTPUT inserted.MessageId
                FROM dbo.Inbox i JOIN cte ON cte.MessageId = i.MessageId;
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Inbox_Ack
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.StringIdList READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL, ProcessedUtc = SYSUTCDATETIME(), LastSeenUtc = SYSUTCDATETIME()
                FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Inbox_Abandon
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.StringIdList READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
                FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Inbox_Fail
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids dbo.StringIdList READONLY,
                @Reason NVARCHAR(MAX) = NULL
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET Status = 'Dead', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
                FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END",

            @"CREATE OR ALTER PROCEDURE dbo.Inbox_ReapExpired
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE dbo.Inbox SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
                WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
                SELECT @@ROWCOUNT AS ReapedCount;
              END",
        };

        foreach (var procedure in procedures)
        {
            await connection.ExecuteAsync(procedure).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that the required database schema exists for the inbox work queue functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureInboxWorkQueueSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Apply inbox work queue migration (columns and types)
            var migrationScript = GetInboxWorkQueueMigrationInlineScript();
            await ExecuteScriptAsync(connection, migrationScript).ConfigureAwait(false);

            // Create each stored procedure individually to avoid batch issues
            await CreateInboxProceduresAsync(connection).ConfigureAwait(false);
        }
        catch (SqlException sqlEx)
        {
            throw new InvalidOperationException($"Failed to ensure inbox work queue schema. SQL Error: {sqlEx.Message} (Error Number: {sqlEx.Number}, Severity: {sqlEx.Class}, State: {sqlEx.State})", sqlEx);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to ensure inbox work queue schema: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the work queue migration script.
    /// </summary>
    /// <returns>The SQL migration script.</returns>
    private static string GetWorkQueueMigrationScript()
    {
        return GetWorkQueueMigrationInlineScript();
    }

    private static string GetWorkQueueMigrationInlineScript()
    {
        return @"
-- Create table-valued parameter types if they don't exist
IF TYPE_ID('dbo.GuidIdList') IS NULL
BEGIN
    CREATE TYPE dbo.GuidIdList AS TABLE (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    );
END

-- Add work queue columns to Outbox if they don't exist
IF COL_LENGTH('dbo.Outbox', 'Status') IS NULL
    ALTER TABLE dbo.Outbox ADD Status TINYINT NOT NULL CONSTRAINT DF_Outbox_Status DEFAULT(0);

IF COL_LENGTH('dbo.Outbox', 'LockedUntil') IS NULL
    ALTER TABLE dbo.Outbox ADD LockedUntil DATETIME2(3) NULL;

IF COL_LENGTH('dbo.Outbox', 'OwnerToken') IS NULL
    ALTER TABLE dbo.Outbox ADD OwnerToken UNIQUEIDENTIFIER NULL;";
    }

    private static string GetInboxWorkQueueMigrationInlineScript()
    {
        return @"
-- Create table-valued parameter types if they don't exist
IF TYPE_ID('dbo.StringIdList') IS NULL
BEGIN
    CREATE TYPE dbo.StringIdList AS TABLE (
        Id VARCHAR(64) NOT NULL PRIMARY KEY
    );
END

-- Add work queue columns to Inbox if they don't exist
IF COL_LENGTH('dbo.Inbox', 'LockedUntil') IS NULL
    ALTER TABLE dbo.Inbox ADD LockedUntil DATETIME2(3) NULL;

IF COL_LENGTH('dbo.Inbox', 'OwnerToken') IS NULL
    ALTER TABLE dbo.Inbox ADD OwnerToken UNIQUEIDENTIFIER NULL;

IF COL_LENGTH('dbo.Inbox', 'Topic') IS NULL
    ALTER TABLE dbo.Inbox ADD Topic VARCHAR(128) NULL;

IF COL_LENGTH('dbo.Inbox', 'Payload') IS NULL
    ALTER TABLE dbo.Inbox ADD Payload NVARCHAR(MAX) NULL;

-- Create work queue index for Inbox if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Inbox_WorkQueue' AND object_id=OBJECT_ID('dbo.Inbox'))
BEGIN
    CREATE INDEX IX_Inbox_WorkQueue ON dbo.Inbox(Status, LastSeenUtc)
        INCLUDE(MessageId, OwnerToken)
        WHERE Status IN ('Seen', 'Processing');
END";
    /// <summary>
    /// Gets the SQL script to create the FanoutPolicy table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetFanoutPolicyCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    -- Primary key columns
    FanoutTopic NVARCHAR(100) NOT NULL,
    WorkKey NVARCHAR(100) NOT NULL,
    
    -- Policy settings
    DefaultEverySeconds INT NOT NULL,
    JitterSeconds INT NOT NULL DEFAULT 60,
    
    -- Auditing
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    CONSTRAINT PK_{tableName} PRIMARY KEY (FanoutTopic, WorkKey)
);

-- Index for efficient lookups by topic (all work keys for a topic)
CREATE INDEX IX_{tableName}_FanoutTopic ON [{schemaName}].[{tableName}](FanoutTopic);";
    }

    /// <summary>
    /// Gets the SQL script to create the FanoutCursor table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetFanoutCursorCreateScript(string schemaName, string tableName)
    {
        return $@"
CREATE TABLE [{schemaName}].[{tableName}] (
    -- Primary key columns
    FanoutTopic NVARCHAR(100) NOT NULL,
    WorkKey NVARCHAR(100) NOT NULL,
    ShardKey NVARCHAR(256) NOT NULL,
    
    -- Cursor data
    LastCompletedAt DATETIMEOFFSET NULL,
    
    -- Auditing
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    CONSTRAINT PK_{tableName} PRIMARY KEY (FanoutTopic, WorkKey, ShardKey)
);

-- Index for efficient queries by topic and work key (all shards for a topic/work combination)
CREATE INDEX IX_{tableName}_TopicWork ON [{schemaName}].[{tableName}](FanoutTopic, WorkKey);

-- Index for finding stale cursors that need processing
CREATE INDEX IX_{tableName}_LastCompleted ON [{schemaName}].[{tableName}](LastCompletedAt)
    WHERE LastCompletedAt IS NOT NULL;";
    }
}