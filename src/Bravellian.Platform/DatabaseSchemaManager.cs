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


using Dapper;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform;
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

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Ensure GuidIdList type exists
        await EnsureGuidIdListTypeAsync(connection, schemaName).ConfigureAwait(false);

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

        // Ensure stored procedures exist
        await EnsureOutboxStoredProceduresAsync(connection, schemaName, tableName).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures that the required database schema exists for the outbox join functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureOutboxJoinSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Check if OutboxJoin table exists
        var joinTableExists = await TableExistsAsync(connection, schemaName, "OutboxJoin").ConfigureAwait(false);
        if (!joinTableExists)
        {
            var createJoinScript = GetOutboxJoinCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createJoinScript).ConfigureAwait(false);
        }

        // Check if OutboxJoinMember table exists
        var memberTableExists = await TableExistsAsync(connection, schemaName, "OutboxJoinMember").ConfigureAwait(false);
        if (!memberTableExists)
        {
            var createMemberScript = GetOutboxJoinMemberCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createMemberScript).ConfigureAwait(false);
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

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Ensure StringIdList type exists
        await EnsureStringIdListTypeAsync(connection, schemaName).ConfigureAwait(false);

        // Check if table exists
        var tableExists = await TableExistsAsync(connection, schemaName, tableName).ConfigureAwait(false);
        if (!tableExists)
        {
            var createScript = GetInboxCreateScript(schemaName, tableName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }
        else
        {
            // Migrate existing tables to add LastError column if it doesn't exist
            await MigrateInboxLastErrorColumnAsync(connection, schemaName, tableName).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureInboxStoredProceduresAsync(connection, schemaName, tableName).ConfigureAwait(false);
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

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

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

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

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
        const string sql = """

                        IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @SchemaName)
                        BEGIN
                            EXEC('CREATE SCHEMA [' + @SchemaName + ']')
                        END
            """;

        await connection.ExecuteAsync(sql, new { SchemaName = schemaName }).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the GuidIdList table type exists in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureGuidIdListTypeAsync(SqlConnection connection, string schemaName)
    {
        // Load script from embedded resource and adapt schema name
        var script = SqlResourceLoader.GetMultiDatabaseTypeScript("GuidIdList");
        var adaptedScript = AdaptSchemaName(script, schemaName);

        // Wrap in IF NOT EXISTS check
        var sql = $"""
            IF TYPE_ID('[{schemaName}].[GuidIdList]') IS NULL
            BEGIN
                {adaptedScript}
            END
            """;

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the StringIdList table type exists in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureStringIdListTypeAsync(SqlConnection connection, string schemaName)
    {
        // Load script from embedded resource and adapt schema name
        var script = SqlResourceLoader.GetMultiDatabaseTypeScript("StringIdList");
        var adaptedScript = AdaptSchemaName(script, schemaName);

        // Wrap in IF NOT EXISTS check
        var sql = $"""
            IF TYPE_ID('[{schemaName}].[StringIdList]') IS NULL
            BEGIN
                {adaptedScript}
            END
            """;

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    /// <summary>
    /// Adapts SQL script to use a different schema name by replacing [infra] with the specified schema.
    /// </summary>
    /// <param name="script">The original SQL script.</param>
    /// <param name="schemaName">The target schema name.</param>
    /// <returns>The adapted SQL script.</returns>
    private static string AdaptSchemaName(string script, string schemaName)
    {
        // Replace [infra] with the target schema name
        return script.Replace("[infra]", $"[{schemaName}]", StringComparison.Ordinal);
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
        const string sql = """

                        SELECT COUNT(1)
                        FROM INFORMATION_SCHEMA.TABLES
                        WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName
            """;

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
        // Load script from embedded resource and adapt schema name
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("Outbox");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "Outbox", also replace the table name
        if (!string.Equals(tableName, "Outbox", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Outbox]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("IX_Outbox_", $"IX_{tableName}_", StringComparison.Ordinal);
        }
        
        return adaptedScript;
    }

    /// <summary>
    /// Gets the SQL script to create the OutboxJoin table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetOutboxJoinCreateScript(string schemaName)
    {
        return $"""

            CREATE TABLE [{schemaName}].[OutboxJoin] (
                -- Core Fields
                JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                PayeWaiveTenantId BIGINT NOT NULL,
                ExpectedSteps INT NOT NULL,
                CompletedSteps INT NOT NULL DEFAULT 0,
                FailedSteps INT NOT NULL DEFAULT 0,
                Status TINYINT NOT NULL DEFAULT 0, -- 0=Pending, 1=Completed, 2=Failed, 3=Cancelled

                -- Timestamps
                CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                LastUpdatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),

                -- Optional metadata (JSON)
                Metadata NVARCHAR(MAX) NULL
            );

            -- Index for querying joins by tenant and status
            CREATE INDEX IX_OutboxJoin_TenantStatus ON [{schemaName}].[OutboxJoin](PayeWaiveTenantId, Status);
            """;
    }

    /// <summary>
    /// Gets the SQL script to create the OutboxJoinMember table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetOutboxJoinMemberCreateScript(string schemaName)
    {
        return $"""

            CREATE TABLE [{schemaName}].[OutboxJoinMember] (
                JoinId UNIQUEIDENTIFIER NOT NULL,
                OutboxMessageId UNIQUEIDENTIFIER NOT NULL,
                CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                CompletedAt DATETIMEOFFSET(3) NULL,
                FailedAt DATETIMEOFFSET(3) NULL,

                -- Composite primary key
                CONSTRAINT PK_OutboxJoinMember PRIMARY KEY (JoinId, OutboxMessageId),

                -- Foreign key to OutboxJoin (cascades deletes)
                CONSTRAINT FK_OutboxJoinMember_Join FOREIGN KEY (JoinId)
                    REFERENCES [{schemaName}].[OutboxJoin](JoinId) ON DELETE CASCADE,

                -- Foreign key to Outbox (enforces referential integrity and cascades deletes)
                CONSTRAINT FK_OutboxJoinMember_Outbox FOREIGN KEY (OutboxMessageId)
                    REFERENCES [{schemaName}].[Outbox](Id) ON DELETE CASCADE
            );

            -- Index for reverse lookup: find all joins for a given message
            CREATE INDEX IX_OutboxJoinMember_MessageId ON [{schemaName}].[OutboxJoinMember](OutboxMessageId);
            """;
    }

    /// <summary>
    /// Gets the SQL script to create the Jobs table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetJobsCreateScript(string schemaName, string tableName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("Jobs");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "Jobs", also replace the table name
        if (!string.Equals(tableName, "Jobs", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Jobs]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("UQ_Jobs_", $"UQ_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_Jobs", $"PK_{tableName}", StringComparison.Ordinal);
        }
        
        return adaptedScript;
    }

    /// <summary>
    /// Gets the SQL script to create the Timers table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetTimersCreateScript(string schemaName, string tableName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("Timers");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "Timers", also replace the table name
        if (!string.Equals(tableName, "Timers", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Timers]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("IX_Timers_", $"IX_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_Timers", $"PK_{tableName}", StringComparison.Ordinal);
        }
        
        return adaptedScript;
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
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("JobRuns");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "JobRuns", also replace the table name
        if (!string.Equals(tableName, "JobRuns", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[JobRuns]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("IX_JobRuns_", $"IX_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_JobRuns", $"PK_{tableName}", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("FK_JobRuns_", $"FK_{tableName}_", StringComparison.Ordinal);
        }
        
        // If jobs table name is not "Jobs", also replace the foreign key reference
        if (!string.Equals(jobsTableName, "Jobs", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Jobs]", $"[{jobsTableName}]", StringComparison.Ordinal);
        }
        
        return adaptedScript;
    }

    /// <summary>
    /// Gets the SQL script to create the DistributedLock table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetDistributedLockCreateScript(string schemaName, string tableName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("DistributedLock");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "DistributedLock", also replace the table name
        if (!string.Equals(tableName, "DistributedLock", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[DistributedLock]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("IX_DistributedLock_", $"IX_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_DistributedLock", $"PK_{tableName}", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("DF_DistributedLock_", $"DF_{tableName}_", StringComparison.Ordinal);
        }
        
        return adaptedScript;
    }

    /// <summary>
    /// Gets the SQL script to create the Lease table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetLeaseCreateScript(string schemaName, string tableName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("Lease");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "Lease", also replace the table name
        if (!string.Equals(tableName, "Lease", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Lease]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_Lease", $"PK_{tableName}", StringComparison.Ordinal);
        }
        
        return adaptedScript;
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
    /// Ensures outbox stored procedures exist.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureOutboxStoredProceduresAsync(SqlConnection connection, string schemaName, string tableName)
    {
        // Create work queue stored procedures
        await CreateOutboxWorkQueueProceduresAsync(connection, schemaName, tableName).ConfigureAwait(false);

        // Create cleanup stored procedure
        var cleanupProc = GetOutboxCleanupStoredProcedure(schemaName, tableName);
        await ExecuteScriptAsync(connection, cleanupProc).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures inbox stored procedures exist.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureInboxStoredProceduresAsync(SqlConnection connection, string schemaName, string tableName)
    {
        // Create work queue stored procedures
        await CreateInboxWorkQueueProceduresAsync(connection, schemaName, tableName).ConfigureAwait(false);

        // Create cleanup stored procedure
        var cleanupProc = GetInboxCleanupStoredProcedure(schemaName, tableName);
        await ExecuteScriptAsync(connection, cleanupProc).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the Lock_Acquire stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockAcquireStoredProcedure(string schemaName)
    {
        return $"""

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

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);
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
            END
            """;
    }

    /// <summary>
    /// Gets the Lock_Renew stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockRenewStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_Renew]
                @ResourceName SYSNAME,
                @OwnerToken UNIQUEIDENTIFIER,
                @LeaseSeconds INT,
                @Renewed BIT OUTPUT,
                @FencingToken BIGINT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

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
            END
            """;
    }

    /// <summary>
    /// Gets the Lock_Release stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockReleaseStoredProcedure(string schemaName)
    {
        return $"""

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
            END
            """;
    }

    /// <summary>
    /// Gets the Lock_CleanupExpired stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLockCleanupStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Lock_CleanupExpired]
            AS
            BEGIN
                SET NOCOUNT ON;
                UPDATE [{schemaName}].[DistributedLock]
                   SET OwnerToken = NULL, LeaseUntil = NULL, ContextJson = NULL
                 WHERE LeaseUntil IS NOT NULL AND LeaseUntil <= SYSDATETIMEOFFSET();
            END
            """;
    }

    /// <summary>
    /// Gets the Lease_Acquire stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLeaseAcquireStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Lease_Acquire]
                @Name SYSNAME,
                @Owner SYSNAME,
                @LeaseSeconds INT,
                @Acquired BIT OUTPUT,
                @ServerUtcNow DATETIMEOFFSET(3) OUTPUT,
                @LeaseUntilUtc DATETIMEOFFSET(3) OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

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
            END
            """;
    }

    /// <summary>
    /// Gets the Lease_Renew stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetLeaseRenewStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Lease_Renew]
                @Name SYSNAME,
                @Owner SYSNAME,
                @LeaseSeconds INT,
                @Renewed BIT OUTPUT,
                @ServerUtcNow DATETIMEOFFSET(3) OUTPUT,
                @LeaseUntilUtc DATETIMEOFFSET(3) OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

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
            END
            """;
    }

    /// <summary>
    /// Gets the Outbox_Cleanup stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetOutboxCleanupStoredProcedure(string schemaName, string tableName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Cleanup]
                @RetentionSeconds INT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @cutoffTime DATETIMEOFFSET = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

                DELETE FROM [{schemaName}].[{tableName}]
                 WHERE IsProcessed = 1
                   AND ProcessedAt IS NOT NULL
                   AND ProcessedAt < @cutoffTime;

                SELECT @@ROWCOUNT AS DeletedCount;
            END
            """;
    }

    /// <summary>
    /// Gets the Inbox_Cleanup stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetInboxCleanupStoredProcedure(string schemaName, string tableName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Cleanup]
                @RetentionSeconds INT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @cutoffTime DATETIMEOFFSET(3) = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

                DELETE FROM [{schemaName}].[{tableName}]
                 WHERE Status = 'Done'
                   AND ProcessedUtc IS NOT NULL
                   AND ProcessedUtc < @cutoffTime;

                SELECT @@ROWCOUNT AS DeletedCount;
            END
            """;
    }

    /// <summary>
    /// Gets the SQL script to create the Inbox table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetInboxCreateScript(string schemaName, string tableName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("Inbox");
        var adaptedScript = AdaptSchemaName(script, schemaName);
        
        // If table name is not "Inbox", also replace the table name
        if (!string.Equals(tableName, "Inbox", StringComparison.Ordinal))
        {
            adaptedScript = adaptedScript.Replace("[Inbox]", $"[{tableName}]", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("IX_Inbox_", $"IX_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("CK_Inbox_", $"CK_{tableName}_", StringComparison.Ordinal);
            adaptedScript = adaptedScript.Replace("PK_Inbox", $"PK_{tableName}", StringComparison.Ordinal);
        }
        
        return adaptedScript;
    }

    /// <summary>
    /// Gets the SQL script to create the OutboxState table for fencing token management.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetOutboxStateCreateScript(string schemaName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("OutboxState");
        return AdaptSchemaName(script, schemaName);
    }

    /// <summary>
    /// Gets the SQL script to create the SchedulerState table for fencing token management.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetSchedulerStateCreateScript(string schemaName)
    {
        var script = SqlResourceLoader.GetMultiDatabaseTableScript("SchedulerState");
        return AdaptSchemaName(script, schemaName);
    }

    /// <summary>
    /// Ensures that the work queue pattern columns and stored procedures exist for the outbox table.
    /// This method is now a wrapper around EnsureOutboxSchemaAsync for backward compatibility.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureWorkQueueSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        // The work queue pattern is now built into the standard Outbox schema
        await EnsureOutboxSchemaAsync(connectionString, schemaName, "Outbox").ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the Outbox work queue stored procedures individually.
    /// </summary>
    /// <param name="connection">The SQL connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CreateOutboxWorkQueueProceduresAsync(SqlConnection connection, string schemaName, string tableName)
    {
        // Create procedures one by one to avoid batch execution issues
        var procedures = new[]
        {
            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Claim]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @LeaseSeconds INT,
                            @BatchSize INT = 50
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                            DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                            WITH cte AS (
                                SELECT TOP (@BatchSize) Id
                                FROM [{schemaName}].[{tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
                                WHERE Status = 0
                                    AND (LockedUntil IS NULL OR LockedUntil <= @now)
                                    AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
                                ORDER BY CreatedAt
                            )
                            UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
                            OUTPUT inserted.Id
                            FROM [{schemaName}].[{tableName}] o JOIN cte ON cte.Id = o.Id;
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Ack]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[GuidIdList] READONLY
                          AS
                          BEGIN
                            SET NOCOUNT ON;

                            -- Mark outbox messages as dispatched
                            UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSDATETIMEOFFSET()
                            FROM [{schemaName}].[{tableName}] o JOIN @Ids i ON i.Id = o.Id
                            WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

                            -- Only proceed with join updates if any messages were actually acknowledged
                            -- and OutboxJoin tables exist (i.e., join feature is enabled)
                            IF @@ROWCOUNT > 0 AND OBJECT_ID(N'[{schemaName}].[OutboxJoinMember]', N'U') IS NOT NULL
                            BEGIN
                                -- First, mark the join members as completed (idempotent via WHERE clause)
                                -- This prevents race conditions by ensuring a member can only be marked once
                                UPDATE m
                                SET CompletedAt = SYSDATETIMEOFFSET()
                                FROM [{schemaName}].[OutboxJoinMember] m
                                INNER JOIN @Ids i
                                    ON m.OutboxMessageId = i.Id
                                WHERE m.CompletedAt IS NULL
                                    AND m.FailedAt IS NULL;

                                -- Then, increment counter ONLY for joins with members that were just marked
                                -- Using @@ROWCOUNT from previous UPDATE ensures we only count newly marked members
                                IF @@ROWCOUNT > 0
                                BEGIN
                                    UPDATE j
                                    SET
                                        CompletedSteps = CompletedSteps + 1,
                                        LastUpdatedUtc = SYSDATETIMEOFFSET()
                                    FROM [{schemaName}].[OutboxJoin] j
                                    INNER JOIN [{schemaName}].[OutboxJoinMember] m
                                        ON j.JoinId = m.JoinId
                                    INNER JOIN @Ids i
                                        ON m.OutboxMessageId = i.Id
                                    WHERE m.CompletedAt IS NOT NULL
                                        AND m.FailedAt IS NULL
                                        AND m.CompletedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                                        AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
                                END
                            END
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Abandon]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[GuidIdList] READONLY,
                            @LastError NVARCHAR(MAX) = NULL,
                            @DueTimeUtc DATETIMEOFFSET(3) = NULL
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE o SET
                                Status = 0,
                                OwnerToken = NULL,
                                LockedUntil = NULL,
                                RetryCount = RetryCount + 1,
                                LastError = ISNULL(@LastError, o.LastError),
                                DueTimeUtc = ISNULL(@DueTimeUtc, o.DueTimeUtc)
                            FROM [{schemaName}].[{tableName}] o JOIN @Ids i ON i.Id = o.Id
                            WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Fail]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[GuidIdList] READONLY,
                            @LastError NVARCHAR(MAX) = NULL,
                            @ProcessedBy NVARCHAR(100) = NULL
                          AS
                          BEGIN
                            SET NOCOUNT ON;

                            -- Mark outbox messages as failed
                            UPDATE o SET
                                Status = 3,
                                OwnerToken = NULL,
                                LockedUntil = NULL,
                                LastError = ISNULL(@LastError, o.LastError),
                                ProcessedBy = ISNULL(@ProcessedBy, o.ProcessedBy)
                            FROM [{schemaName}].[{tableName}] o JOIN @Ids i ON i.Id = o.Id
                            WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

                            -- Only proceed with join updates if any messages were actually failed
                            -- and OutboxJoin tables exist (i.e., join feature is enabled)
                            IF @@ROWCOUNT > 0 AND OBJECT_ID(N'[{schemaName}].[OutboxJoinMember]', N'U') IS NOT NULL
                            BEGIN
                                -- First, mark the join members as failed (idempotent via WHERE clause)
                                -- This prevents race conditions by ensuring a member can only be marked once
                                UPDATE m
                                SET FailedAt = SYSDATETIMEOFFSET()
                                FROM [{schemaName}].[OutboxJoinMember] m
                                INNER JOIN @Ids i
                                    ON m.OutboxMessageId = i.Id
                                WHERE m.CompletedAt IS NULL
                                    AND m.FailedAt IS NULL;

                                -- Then, increment counter ONLY for joins with members that were just marked
                                -- Using @@ROWCOUNT from previous UPDATE ensures we only count newly marked members
                                IF @@ROWCOUNT > 0
                                BEGIN
                                    UPDATE j
                                    SET
                                        FailedSteps = FailedSteps + 1,
                                        LastUpdatedUtc = SYSDATETIMEOFFSET()
                                    FROM [{schemaName}].[OutboxJoin] j
                                    INNER JOIN [{schemaName}].[OutboxJoinMember] m
                                        ON j.JoinId = m.JoinId
                                    INNER JOIN @Ids i
                                        ON m.OutboxMessageId = i.Id
                                    WHERE m.CompletedAt IS NULL
                                        AND m.FailedAt IS NOT NULL
                                        AND m.FailedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                                        AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
                                END
                            END
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_ReapExpired]
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE [{schemaName}].[{tableName}] SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
                            WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSDATETIMEOFFSET();
                            SELECT @@ROWCOUNT AS ReapedCount;
                          END
            """,
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
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task CreateInboxWorkQueueProceduresAsync(SqlConnection connection, string schemaName, string tableName)
    {
        // Create procedures one by one to avoid batch execution issues
        var procedures = new[]
        {
            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Claim]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @LeaseSeconds INT,
                            @BatchSize INT = 50
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                            DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                            WITH cte AS (
                                SELECT TOP (@BatchSize) MessageId
                                FROM [{schemaName}].[{tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
                                WHERE Status IN ('Seen', 'Processing')
                                    AND (LockedUntil IS NULL OR LockedUntil <= @now)
                                    AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
                                ORDER BY LastSeenUtc
                            )
                            UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
                            OUTPUT inserted.MessageId
                            FROM [{schemaName}].[{tableName}] i JOIN cte ON cte.MessageId = i.MessageId;
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Ack]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[StringIdList] READONLY
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL, ProcessedUtc = SYSDATETIMEOFFSET(), LastSeenUtc = SYSDATETIMEOFFSET()
                            FROM [{schemaName}].[{tableName}] i JOIN @Ids ids ON ids.Id = i.MessageId
                            WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Abandon]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[StringIdList] READONLY,
                            @LastError NVARCHAR(MAX) = NULL,
                            @DueTimeUtc DATETIMEOFFSET(3) = NULL
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE i SET
                                Status = 'Seen',
                                OwnerToken = NULL,
                                LockedUntil = NULL,
                                LastSeenUtc = SYSDATETIMEOFFSET(),
                                Attempts = Attempts + 1,
                                LastError = ISNULL(@LastError, i.LastError),
                                DueTimeUtc = ISNULL(@DueTimeUtc, i.DueTimeUtc)
                            FROM [{schemaName}].[{tableName}] i JOIN @Ids ids ON ids.Id = i.MessageId
                            WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_Fail]
                            @OwnerToken UNIQUEIDENTIFIER,
                            @Ids [{schemaName}].[StringIdList] READONLY,
                            @Reason NVARCHAR(MAX) = NULL
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE i SET
                                Status = 'Dead',
                                OwnerToken = NULL,
                                LockedUntil = NULL,
                                LastSeenUtc = SYSDATETIMEOFFSET(),
                                LastError = ISNULL(@Reason, i.LastError)
                            FROM [{schemaName}].[{tableName}] i JOIN @Ids ids ON ids.Id = i.MessageId
                            WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
                          END
            """,

            $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[{tableName}_ReapExpired]
                          AS
                          BEGIN
                            SET NOCOUNT ON;
                            UPDATE [{schemaName}].[{tableName}] SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSDATETIMEOFFSET()
                            WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSDATETIMEOFFSET();
                            SELECT @@ROWCOUNT AS ReapedCount;
                          END
            """,
        };

        foreach (var procedure in procedures)
        {
            await connection.ExecuteAsync(procedure).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures that the required database schema exists for the inbox work queue functionality.
    /// This method is now a wrapper around EnsureInboxSchemaAsync for backward compatibility.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureInboxWorkQueueSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        // The work queue pattern is now built into the standard Inbox schema
        await EnsureInboxSchemaAsync(connectionString, schemaName, "Inbox").ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the SQL script to create the FanoutPolicy table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetFanoutPolicyCreateScript(string schemaName, string tableName)
    {
        return $"""

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
            CREATE INDEX IX_{tableName}_FanoutTopic ON [{schemaName}].[{tableName}](FanoutTopic);
            """;
    }

    /// <summary>
    /// Gets the SQL script to create the FanoutCursor table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetFanoutCursorCreateScript(string schemaName, string tableName)
    {
        return $"""

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
                WHERE LastCompletedAt IS NOT NULL;
            """;
    }

    /// <summary>
    /// Ensures that the required database schema exists for the semaphore functionality.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureSemaphoreSchemaAsync(string connectionString, string schemaName = "dbo")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Check if Semaphore table exists
        var semaphoreExists = await TableExistsAsync(connection, schemaName, "Semaphore").ConfigureAwait(false);
        if (!semaphoreExists)
        {
            var createScript = GetSemaphoreCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Check if SemaphoreLease table exists
        var leaseExists = await TableExistsAsync(connection, schemaName, "SemaphoreLease").ConfigureAwait(false);
        if (!leaseExists)
        {
            var createScript = GetSemaphoreLeaseCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureSemaphoreStoredProceduresAsync(connection, schemaName).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the SQL script to create the Semaphore table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetSemaphoreCreateScript(string schemaName)
    {
        return $"""

            CREATE TABLE [{schemaName}].[Semaphore] (
                [Name] NVARCHAR(200) NOT NULL CONSTRAINT PK_Semaphore PRIMARY KEY,
                [Limit] INT NOT NULL,
                [NextFencingCounter] BIGINT NOT NULL DEFAULT 1,
                [UpdatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET()
            );
            """;
    }

    /// <summary>
    /// Gets the SQL script to create the SemaphoreLease table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The SQL create script.</returns>
    private static string GetSemaphoreLeaseCreateScript(string schemaName)
    {
        return $"""

            CREATE TABLE [{schemaName}].[SemaphoreLease] (
                [Name] NVARCHAR(200) NOT NULL,
                [Token] UNIQUEIDENTIFIER NOT NULL,
                [Fencing] BIGINT NOT NULL,
                [OwnerId] NVARCHAR(200) NOT NULL,
                [LeaseUntilUtc] DATETIMEOFFSET(3) NOT NULL,
                [CreatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                [RenewedUtc] DATETIMEOFFSET(3) NULL,
                [ClientRequestId] NVARCHAR(100) NULL,
                CONSTRAINT PK_SemaphoreLease PRIMARY KEY ([Name], [Token])
            );

            -- Index for efficient counting of active leases
            CREATE INDEX IX_SemaphoreLease_Name_LeaseUntilUtc
                ON [{schemaName}].[SemaphoreLease]([Name], [LeaseUntilUtc])
                INCLUDE([Token]);

            -- Index for reaping expired leases
            CREATE INDEX IX_SemaphoreLease_LeaseUntilUtc
                ON [{schemaName}].[SemaphoreLease]([LeaseUntilUtc]);

            -- Index for idempotent acquire lookups by client request ID
            CREATE INDEX IX_SemaphoreLease_ClientRequestId
                ON [{schemaName}].[SemaphoreLease]([ClientRequestId])
                WHERE [ClientRequestId] IS NOT NULL;
            """;
    }

    /// <summary>
    /// Ensures semaphore stored procedures exist.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task EnsureSemaphoreStoredProceduresAsync(SqlConnection connection, string schemaName)
    {
        var acquireProc = GetSemaphoreAcquireStoredProcedure(schemaName);
        var renewProc = GetSemaphoreRenewStoredProcedure(schemaName);
        var releaseProc = GetSemaphoreReleaseStoredProcedure(schemaName);
        var reapProc = GetSemaphoreReapStoredProcedure(schemaName);

        await ExecuteScriptAsync(connection, acquireProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, renewProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, releaseProc).ConfigureAwait(false);
        await ExecuteScriptAsync(connection, reapProc).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the Semaphore_Acquire stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetSemaphoreAcquireStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Semaphore_Acquire]
                @Name NVARCHAR(200),
                @OwnerId NVARCHAR(200),
                @TtlSeconds INT,
                @ClientRequestId NVARCHAR(100) = NULL,
                @Acquired BIT OUTPUT,
                @Token UNIQUEIDENTIFIER OUTPUT,
                @Fencing BIGINT OUTPUT,
                @ExpiresAtUtc DATETIMEOFFSET(3) OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @TtlSeconds, @now);
                DECLARE @activeCount INT;
                DECLARE @limit INT;

                BEGIN TRAN;

                -- Lock semaphore row for this name
                SELECT @limit = [Limit]
                FROM [{schemaName}].[Semaphore] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Name] = @Name;

                -- If semaphore doesn't exist, fail
                IF @limit IS NULL
                BEGIN
                    SET @Acquired = 0;
                    SET @Token = NULL;
                    SET @Fencing = NULL;
                    SET @ExpiresAtUtc = NULL;
                    COMMIT TRAN;
                    RETURN;
                END

                -- Check if we have an existing lease for this client request ID
                IF @ClientRequestId IS NOT NULL
                BEGIN
                    SELECT @Token = [Token], @Fencing = [Fencing], @ExpiresAtUtc = [LeaseUntilUtc]
                    FROM [{schemaName}].[SemaphoreLease]
                    WHERE [Name] = @Name
                        AND [ClientRequestId] = @ClientRequestId
                        AND [LeaseUntilUtc] > @now;

                    IF @Token IS NOT NULL
                    BEGIN
                        SET @Acquired = 1;
                        COMMIT TRAN;
                        RETURN;
                    END
                END

                -- Opportunistic reap: delete a small batch of expired leases
                DELETE TOP (10) FROM [{schemaName}].[SemaphoreLease]
                WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;

                -- Count active leases
                SELECT @activeCount = COUNT(*)
                FROM [{schemaName}].[SemaphoreLease]
                WHERE [Name] = @Name AND [LeaseUntilUtc] > @now;

                -- Check if we can acquire
                IF @activeCount >= @limit
                BEGIN
                    SET @Acquired = 0;
                    SET @Token = NULL;
                    SET @Fencing = NULL;
                    SET @ExpiresAtUtc = NULL;
                    COMMIT TRAN;
                    RETURN;
                END

                -- Acquire the lease
                SET @Token = NEWID();

                -- Get and increment fencing counter
                UPDATE [{schemaName}].[Semaphore]
                SET @Fencing = [NextFencingCounter],
                    [NextFencingCounter] = [NextFencingCounter] + 1,
                    [UpdatedUtc] = @now
                WHERE [Name] = @Name;

                -- Insert lease
                INSERT INTO [{schemaName}].[SemaphoreLease]
                    ([Name], [Token], [Fencing], [OwnerId], [LeaseUntilUtc], [CreatedUtc], [ClientRequestId])
                VALUES
                    (@Name, @Token, @Fencing, @OwnerId, @until, @now, @ClientRequestId);

                SET @Acquired = 1;
                SET @ExpiresAtUtc = @until;

                COMMIT TRAN;
            END
            """;
    }

    /// <summary>
    /// Gets the Semaphore_Renew stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetSemaphoreRenewStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Semaphore_Renew]
                @Name NVARCHAR(200),
                @Token UNIQUEIDENTIFIER,
                @TtlSeconds INT,
                @Renewed BIT OUTPUT,
                @ExpiresAtUtc DATETIMEOFFSET(3) OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @TtlSeconds, @now);
                DECLARE @currentExpiry DATETIMEOFFSET(3);

                -- Check current expiry
                SELECT @currentExpiry = [LeaseUntilUtc]
                FROM [{schemaName}].[SemaphoreLease]
                WHERE [Name] = @Name AND [Token] = @Token;

                -- If not found or expired, return Lost
                IF @currentExpiry IS NULL OR @currentExpiry <= @now
                BEGIN
                    SET @Renewed = 0;
                    SET @ExpiresAtUtc = NULL;
                    RETURN;
                END

                -- Monotonic extension: only extend if new expiry is later
                IF @until > @currentExpiry
                BEGIN
                    SET @ExpiresAtUtc = @until;
                END
                ELSE
                BEGIN
                    SET @ExpiresAtUtc = @currentExpiry;
                END

                -- Update lease
                UPDATE [{schemaName}].[SemaphoreLease]
                SET [LeaseUntilUtc] = @ExpiresAtUtc,
                    [RenewedUtc] = @now
                WHERE [Name] = @Name AND [Token] = @Token;

                SET @Renewed = 1;
            END
            """;
    }

    /// <summary>
    /// Gets the Semaphore_Release stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetSemaphoreReleaseStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Semaphore_Release]
                @Name NVARCHAR(200),
                @Token UNIQUEIDENTIFIER,
                @Released BIT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DELETE FROM [{schemaName}].[SemaphoreLease]
                WHERE [Name] = @Name AND [Token] = @Token;

                IF @@ROWCOUNT > 0
                BEGIN
                    SET @Released = 1;
                END
                ELSE
                BEGIN
                    SET @Released = 0;
                END
            END
            """;
    }

    /// <summary>
    /// Gets the Semaphore_Reap stored procedure script.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>The stored procedure script.</returns>
    private static string GetSemaphoreReapStoredProcedure(string schemaName)
    {
        return $"""

            CREATE OR ALTER PROCEDURE [{schemaName}].[Semaphore_Reap]
                @Name NVARCHAR(200) = NULL,
                @MaxRows INT = 1000,
                @DeletedCount INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();

                IF @Name IS NULL
                BEGIN
                    -- Reap across all semaphores
                    DELETE TOP (@MaxRows) FROM [{schemaName}].[SemaphoreLease]
                    WHERE [LeaseUntilUtc] <= @now;
                END
                ELSE
                BEGIN
                    -- Reap for specific semaphore
                    DELETE TOP (@MaxRows) FROM [{schemaName}].[SemaphoreLease]
                    WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;
                END

                SET @DeletedCount = @@ROWCOUNT;
            END
            """;
    }

    /// <summary>
    /// Ensures that the required database schema exists for the metrics functionality in application databases.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "infra").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureMetricsSchemaAsync(string connectionString, string schemaName = "infra")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Create MetricDef table
        var metricDefExists = await TableExistsAsync(connection, schemaName, "MetricDef").ConfigureAwait(false);
        if (!metricDefExists)
        {
            var createScript = GetMetricDefCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create MetricSeries table
        var metricSeriesExists = await TableExistsAsync(connection, schemaName, "MetricSeries").ConfigureAwait(false);
        if (!metricSeriesExists)
        {
            var createScript = GetMetricSeriesCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create MetricPointMinute table
        var metricPointExists = await TableExistsAsync(connection, schemaName, "MetricPointMinute").ConfigureAwait(false);
        if (!metricPointExists)
        {
            var createScript = GetMetricPointMinuteCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureMetricsStoredProceduresAsync(connection, schemaName).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures that the required database schema exists for the metrics functionality in the central database.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "infra").</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnsureCentralMetricsSchemaAsync(string connectionString, string schemaName = "infra")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Ensure schema exists
        await EnsureSchemaExistsAsync(connection, schemaName).ConfigureAwait(false);

        // Create MetricDef table
        var metricDefExists = await TableExistsAsync(connection, schemaName, "MetricDef").ConfigureAwait(false);
        if (!metricDefExists)
        {
            var createScript = GetMetricDefCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create central MetricSeries table (with DatabaseId for cross-database aggregation)
        var metricSeriesExists = await TableExistsAsync(connection, schemaName, "MetricSeries").ConfigureAwait(false);
        if (!metricSeriesExists)
        {
            var createScript = GetCentralMetricSeriesCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create MetricPointHourly table
        var metricPointExists = await TableExistsAsync(connection, schemaName, "MetricPointHourly").ConfigureAwait(false);
        if (!metricPointExists)
        {
            var createScript = GetMetricPointHourlyCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Create ExporterHeartbeat table
        var heartbeatExists = await TableExistsAsync(connection, schemaName, "ExporterHeartbeat").ConfigureAwait(false);
        if (!heartbeatExists)
        {
            var createScript = GetExporterHeartbeatCreateScript(schemaName);
            await ExecuteScriptAsync(connection, createScript).ConfigureAwait(false);
        }

        // Ensure stored procedures exist
        await EnsureCentralMetricsStoredProceduresAsync(connection, schemaName).ConfigureAwait(false);
    }

    private static async Task EnsureMetricsStoredProceduresAsync(SqlConnection connection, string schemaName)
    {
        var spUpsertSeries = GetSpUpsertSeriesScript(schemaName);
        await ExecuteScriptAsync(connection, spUpsertSeries).ConfigureAwait(false);

        var spUpsertMetricPoint = GetSpUpsertMetricPointMinuteScript(schemaName);
        await ExecuteScriptAsync(connection, spUpsertMetricPoint).ConfigureAwait(false);
    }

    private static async Task EnsureCentralMetricsStoredProceduresAsync(SqlConnection connection, string schemaName)
    {
        var spUpsertSeries = GetSpUpsertSeriesCentralScript(schemaName);
        await ExecuteScriptAsync(connection, spUpsertSeries).ConfigureAwait(false);

        var spUpsertMetricPoint = GetSpUpsertMetricPointHourlyScript(schemaName);
        await ExecuteScriptAsync(connection, spUpsertMetricPoint).ConfigureAwait(false);
    }

    private static string GetMetricDefCreateScript(string schemaName)
    {
        return $"""
            CREATE TABLE [{schemaName}].[MetricDef] (
              MetricDefId   INT IDENTITY PRIMARY KEY,
              Name          NVARCHAR(128) NOT NULL UNIQUE,
              Unit          NVARCHAR(32)  NOT NULL,
              AggKind       NVARCHAR(16)  NOT NULL,
              Description   NVARCHAR(512) NOT NULL
            );
            """;
    }

    private static string GetMetricSeriesCreateScript(string schemaName)
    {
        return $$"""
            CREATE TABLE [{{schemaName}}].[MetricSeries] (
              SeriesId      BIGINT IDENTITY PRIMARY KEY,
              MetricDefId   INT NOT NULL REFERENCES [{{schemaName}}].[MetricDef](MetricDefId),
              Service       NVARCHAR(64) NOT NULL,
              InstanceId    UNIQUEIDENTIFIER NOT NULL,
              TagsJson      NVARCHAR(1024) NOT NULL DEFAULT (N'{}'),
              TagHash       VARBINARY(32) NOT NULL,
              CreatedUtc    DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
              CONSTRAINT UQ_MetricSeries UNIQUE (MetricDefId, Service, InstanceId, TagHash)
            );
            """;
    }

    private static string GetMetricPointMinuteCreateScript(string schemaName)
    {
        return $"""
            CREATE TABLE [{schemaName}].[MetricPointMinute] (
              SeriesId        BIGINT       NOT NULL REFERENCES [{schemaName}].[MetricSeries](SeriesId),
              BucketStartUtc  DATETIMEOFFSET(0) NOT NULL,
              BucketSecs      SMALLINT     NOT NULL,
              ValueSum        FLOAT        NULL,
              ValueCount      INT          NULL,
              ValueMin        FLOAT        NULL,
              ValueMax        FLOAT        NULL,
              ValueLast       FLOAT        NULL,
              P50             FLOAT        NULL,
              P95             FLOAT        NULL,
              P99             FLOAT        NULL,
              InsertedUtc     DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
              CONSTRAINT PK_MetricPointMinute PRIMARY KEY (SeriesId, BucketStartUtc, BucketSecs)
            );

            CREATE INDEX IX_MetricPointMinute_ByTime ON [{schemaName}].[MetricPointMinute] (BucketStartUtc)
              INCLUDE (SeriesId, ValueSum, ValueCount, P95);
            """;
    }

    private static string GetCentralMetricSeriesCreateScript(string schemaName)
    {
        return $"""
            CREATE TABLE [{schemaName}].[MetricSeries] (
              SeriesId      BIGINT IDENTITY PRIMARY KEY,
              MetricDefId   INT NOT NULL REFERENCES [{schemaName}].[MetricDef](MetricDefId),
              DatabaseId    UNIQUEIDENTIFIER NULL,
              Service       NVARCHAR(64) NOT NULL,
              TagsJson      NVARCHAR(1024) NOT NULL DEFAULT N'{"{"}"{"}"}',
              TagHash       VARBINARY(32)  NOT NULL,
              CreatedUtc    DATETIMEOFFSET(3)   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
              CONSTRAINT UQ_MetricSeries UNIQUE (MetricDefId, DatabaseId, Service, TagHash)
            );
            """;
    }

    private static string GetMetricPointHourlyCreateScript(string schemaName)
    {
        return $"""
            CREATE TABLE [{schemaName}].[MetricPointHourly] (
              SeriesId        BIGINT       NOT NULL REFERENCES [{schemaName}].[MetricSeries](SeriesId),
              BucketStartUtc  DATETIMEOFFSET(0) NOT NULL,
              BucketSecs      INT          NOT NULL,
              ValueSum        FLOAT        NULL,
              ValueCount      INT          NULL,
              ValueMin        FLOAT        NULL,
              ValueMax        FLOAT        NULL,
              ValueLast       FLOAT        NULL,
              P50             FLOAT        NULL,
              P95             FLOAT        NULL,
              P99             FLOAT        NULL,
              InsertedUtc     DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
              CONSTRAINT PK_MetricPointHourly PRIMARY KEY NONCLUSTERED (SeriesId, BucketStartUtc, BucketSecs)
            );

            CREATE CLUSTERED COLUMNSTORE INDEX CCI_MetricPointHourly ON [{schemaName}].[MetricPointHourly];
            """;
    }

    private static string GetExporterHeartbeatCreateScript(string schemaName)
    {
        return $"""
            CREATE TABLE [{schemaName}].[ExporterHeartbeat] (
              InstanceId    NVARCHAR(100) NOT NULL PRIMARY KEY,
              LastFlushUtc  DATETIMEOFFSET(3)  NOT NULL,
              LastError     NVARCHAR(512) NULL
            );
            """;
    }

    private static string GetSpUpsertSeriesScript(string schemaName)
    {
        return $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[SpUpsertSeries]
              @Name NVARCHAR(128),
              @Unit NVARCHAR(32),
              @AggKind NVARCHAR(16),
              @Description NVARCHAR(512),
              @Service NVARCHAR(64),
              @InstanceId UNIQUEIDENTIFIER,
              @TagsJson NVARCHAR(1024),
              @TagHash VARBINARY(32),
              @SeriesId BIGINT OUTPUT
            AS
            BEGIN
              SET NOCOUNT ON;
              DECLARE @MetricDefId INT;

              SELECT @MetricDefId = MetricDefId FROM [{schemaName}].[MetricDef] WHERE Name = @Name;
              IF @MetricDefId IS NULL
              BEGIN
                INSERT INTO [{schemaName}].[MetricDef](Name, Unit, AggKind, Description)
                VALUES(@Name, @Unit, @AggKind, @Description);
                SET @MetricDefId = SCOPE_IDENTITY();
              END

              MERGE [{schemaName}].[MetricSeries] WITH (HOLDLOCK) AS T
              USING (SELECT @MetricDefId AS MetricDefId, @Service AS Service, @InstanceId AS InstanceId, @TagHash AS TagHash) AS S
                ON (T.MetricDefId = S.MetricDefId AND T.Service = S.Service AND T.InstanceId = S.InstanceId AND T.TagHash = S.TagHash)
              WHEN MATCHED THEN
                UPDATE SET TagsJson = @TagsJson
              WHEN NOT MATCHED THEN
                INSERT (MetricDefId, Service, InstanceId, TagsJson, TagHash)
                VALUES(@MetricDefId, @Service, @InstanceId, @TagsJson, @TagHash);

              SELECT @SeriesId = SeriesId FROM [{schemaName}].[MetricSeries]
              WHERE MetricDefId = @MetricDefId AND Service = @Service AND InstanceId = @InstanceId AND TagHash = @TagHash;
            END
            """;
    }

    private static string GetSpUpsertMetricPointMinuteScript(string schemaName)
    {
        return $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[SpUpsertMetricPointMinute]
              @SeriesId BIGINT,
              @BucketStartUtc DATETIMEOFFSET(0),
              @BucketSecs SMALLINT,
              @ValueSum FLOAT,
              @ValueCount INT,
              @ValueMin FLOAT,
              @ValueMax FLOAT,
              @ValueLast FLOAT,
              @P50 FLOAT = NULL,
              @P95 FLOAT = NULL,
              @P99 FLOAT = NULL
            AS
            BEGIN
              SET NOCOUNT ON;

              DECLARE @LockRes INT;
              DECLARE @ResourceName NVARCHAR(255) = CONCAT('infra:mpm:', @SeriesId, ':', CONVERT(VARCHAR(19), @BucketStartUtc, 126), ':', @BucketSecs);

              EXEC @LockRes = sp_getapplock
                @Resource = @ResourceName,
                @LockMode = 'Exclusive',
                @LockTimeout = 5000,
                @DbPrincipal = 'public';

              IF @LockRes < 0 RETURN;

              IF EXISTS (SELECT 1 FROM [{schemaName}].[MetricPointMinute] WITH (UPDLOCK, HOLDLOCK)
                         WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs)
              BEGIN
                -- Do not update percentiles on merge; percentiles cannot be accurately combined
                UPDATE [{schemaName}].[MetricPointMinute]
                  SET ValueSum   = ISNULL(ValueSum,0)   + ISNULL(@ValueSum,0),
                      ValueCount = ISNULL(ValueCount,0) + ISNULL(@ValueCount,0),
                      ValueMin   = CASE WHEN ValueMin IS NULL OR @ValueMin < ValueMin THEN @ValueMin ELSE ValueMin END,
                      ValueMax   = CASE WHEN ValueMax IS NULL OR @ValueMax > ValueMax THEN @ValueMax ELSE ValueMax END,
                      ValueLast  = @ValueLast,
                      InsertedUtc = SYSDATETIMEOFFSET()
                WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs;
              END
              ELSE
              BEGIN
                INSERT INTO [{schemaName}].[MetricPointMinute](SeriesId, BucketStartUtc, BucketSecs,
                  ValueSum, ValueCount, ValueMin, ValueMax, ValueLast, P50, P95, P99)
                VALUES(@SeriesId, @BucketStartUtc, @BucketSecs,
                  @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast, @P50, @P95, @P99);
              END

              EXEC sp_releaseapplock @Resource = @ResourceName, @DbPrincipal='public';
            END
            """;
    }

    private static string GetSpUpsertSeriesCentralScript(string schemaName)
    {
        return $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[SpUpsertSeriesCentral]
              @Name NVARCHAR(128),
              @Unit NVARCHAR(32),
              @AggKind NVARCHAR(16),
              @Description NVARCHAR(512),
              @DatabaseId UNIQUEIDENTIFIER,
              @Service NVARCHAR(64),
              @TagsJson NVARCHAR(1024),
              @TagHash VARBINARY(32),
              @SeriesId BIGINT OUTPUT
            AS
            BEGIN
              SET NOCOUNT ON;
              DECLARE @MetricDefId INT;

              SELECT @MetricDefId = MetricDefId FROM [{schemaName}].[MetricDef] WHERE Name = @Name;
              IF @MetricDefId IS NULL
              BEGIN
                INSERT INTO [{schemaName}].[MetricDef](Name, Unit, AggKind, Description)
                VALUES(@Name, @Unit, @AggKind, @Description);
                SET @MetricDefId = SCOPE_IDENTITY();
              END

              MERGE [{schemaName}].[MetricSeries] WITH (HOLDLOCK) AS T
              USING (SELECT @MetricDefId AS MetricDefId, @DatabaseId AS DatabaseId, @Service AS Service, @TagHash AS TagHash) AS S
                ON (T.MetricDefId = S.MetricDefId AND T.DatabaseId = S.DatabaseId AND T.Service = S.Service AND T.TagHash = S.TagHash)
              WHEN MATCHED THEN
                UPDATE SET TagsJson = @TagsJson
              WHEN NOT MATCHED THEN
                INSERT (MetricDefId, DatabaseId, Service, TagsJson, TagHash)
                VALUES(@MetricDefId, @DatabaseId, @Service, @TagsJson, @TagHash);

              SELECT @SeriesId = SeriesId FROM [{schemaName}].[MetricSeries]
              WHERE MetricDefId = @MetricDefId AND DatabaseId = @DatabaseId AND Service = @Service AND TagHash = @TagHash;
            END
            """;
    }

    private static string GetSpUpsertMetricPointHourlyScript(string schemaName)
    {
        return $"""
            CREATE OR ALTER PROCEDURE [{schemaName}].[SpUpsertMetricPointHourly]
              @SeriesId BIGINT,
              @BucketStartUtc DATETIMEOFFSET(0),
              @BucketSecs INT,
              @ValueSum FLOAT,
              @ValueCount INT,
              @ValueMin FLOAT,
              @ValueMax FLOAT,
              @ValueLast FLOAT,
              @P50 FLOAT = NULL,
              @P95 FLOAT = NULL,
              @P99 FLOAT = NULL
            AS
            BEGIN
              SET NOCOUNT ON;

              DECLARE @LockRes INT;
              DECLARE @ResourceName NVARCHAR(255) = CONCAT('infra:mph:', @SeriesId, ':', CONVERT(VARCHAR(19), @BucketStartUtc, 126), ':', @BucketSecs);

              EXEC @LockRes = sp_getapplock
                @Resource = @ResourceName,
                @LockMode = 'Exclusive',
                @LockTimeout = 5000,
                @DbPrincipal = 'public';

              IF @LockRes < 0 RETURN;

              IF EXISTS (SELECT 1 FROM [{schemaName}].[MetricPointHourly] WITH (UPDLOCK, HOLDLOCK)
                         WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs)
              BEGIN
                -- Do not update percentiles on merge; percentiles cannot be accurately combined
                UPDATE [{schemaName}].[MetricPointHourly]
                  SET ValueSum   = ISNULL(ValueSum,0)   + ISNULL(@ValueSum,0),
                      ValueCount = ISNULL(ValueCount,0) + ISNULL(@ValueCount,0),
                      ValueMin   = CASE WHEN ValueMin IS NULL OR @ValueMin < ValueMin THEN @ValueMin ELSE ValueMin END,
                      ValueMax   = CASE WHEN ValueMax IS NULL OR @ValueMax > ValueMax THEN @ValueMax ELSE ValueMax END,
                      ValueLast  = @ValueLast,
                      InsertedUtc = SYSDATETIMEOFFSET()
                WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs;
              END
              ELSE
              BEGIN
                INSERT INTO [{schemaName}].[MetricPointHourly](SeriesId, BucketStartUtc, BucketSecs,
                  ValueSum, ValueCount, ValueMin, ValueMax, ValueLast, P50, P95, P99)
                VALUES(@SeriesId, @BucketStartUtc, @BucketSecs,
                  @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast, @P50, @P95, @P99);
              END

              EXEC sp_releaseapplock @Resource = @ResourceName, @DbPrincipal='public';
            END
            """;
    }

    /// <summary>
    /// Migrates existing Inbox tables to add the LastError column if it doesn't exist.
    /// This supports upgrading from the old schema to the new work queue pattern.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task MigrateInboxLastErrorColumnAsync(SqlConnection connection, string schemaName, string tableName)
    {
        var sql = $"""
            IF COL_LENGTH('[{schemaName}].[{tableName}]', 'LastError') IS NULL
            BEGIN
                ALTER TABLE [{schemaName}].[{tableName}] ADD LastError NVARCHAR(MAX) NULL;
            END
            """;

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }
}
