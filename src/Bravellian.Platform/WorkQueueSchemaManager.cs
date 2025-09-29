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
/// Manages work queue schema and stored procedures for SQL Server tables.
/// </summary>
internal static class WorkQueueSchemaManager
{
    /// <summary>
    /// Ensures work queue schema exists for a table with CreatedAt ordering.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="schemaName">Schema name.</param>
    /// <param name="tableName">Table name.</param>
    /// <param name="primaryKeyColumn">Primary key column name.</param>
    /// <param name="primaryKeyType">Primary key SQL type (e.g., "UNIQUEIDENTIFIER", "BIGINT").</param>
    /// <param name="orderingColumn">Column used for ordering (default: "CreatedAt").</param>
    /// <returns>Task representing the async operation.</returns>
    public static async Task EnsureWorkQueueSchemaAsync(
        string connectionString,
        string schemaName,
        string tableName,
        string primaryKeyColumn,
        string primaryKeyType,
        string orderingColumn = "CreatedAt")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Add work queue columns if missing
        await EnsureWorkQueueColumnsAsync(connection, schemaName, tableName).ConfigureAwait(false);

        // Create index for work queue operations
        await EnsureWorkQueueIndexAsync(connection, schemaName, tableName, orderingColumn).ConfigureAwait(false);

        // Create TVP if needed
        await EnsureIdListTvpAsync(connection, primaryKeyType).ConfigureAwait(false);

        // Create stored procedures
        await CreateClaimProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, orderingColumn).ConfigureAwait(false);
        await CreateAckProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateAbandonProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateFailProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateReapExpiredProcedureAsync(connection, schemaName, tableName).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures work queue schema exists for a table with DueTime/DueAt ordering (scheduled items).
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="schemaName">Schema name.</param>
    /// <param name="tableName">Table name.</param>
    /// <param name="primaryKeyColumn">Primary key column name.</param>
    /// <param name="primaryKeyType">Primary key SQL type.</param>
    /// <param name="dueTimeColumn">Column containing due time (default: "DueTime").</param>
    /// <returns>Task representing the async operation.</returns>
    public static async Task EnsureScheduledWorkQueueSchemaAsync(
        string connectionString,
        string schemaName,
        string tableName,
        string primaryKeyColumn,
        string primaryKeyType,
        string dueTimeColumn = "DueTime")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Add work queue columns if missing
        await EnsureWorkQueueColumnsAsync(connection, schemaName, tableName).ConfigureAwait(false);

        // Create index for scheduled work queue operations
        await EnsureScheduledWorkQueueIndexAsync(connection, schemaName, tableName, dueTimeColumn).ConfigureAwait(false);

        // Create TVP if needed
        await EnsureIdListTvpAsync(connection, primaryKeyType).ConfigureAwait(false);

        // Create stored procedures for scheduled items
        await CreateClaimDueProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, dueTimeColumn).ConfigureAwait(false);
        await CreateAckProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateAbandonProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateFailProcedureAsync(connection, schemaName, tableName, primaryKeyColumn, primaryKeyType).ConfigureAwait(false);
        await CreateReapExpiredProcedureAsync(connection, schemaName, tableName).ConfigureAwait(false);
    }

    private static async Task EnsureWorkQueueColumnsAsync(SqlConnection connection, string schemaName, string tableName)
    {
        var addColumnsScript = $@"
            -- Add Status column if missing
            IF COL_LENGTH('[{schemaName}].[{tableName}]', 'Status') IS NULL
                ALTER TABLE [{schemaName}].[{tableName}] ADD Status TINYINT NOT NULL CONSTRAINT DF_{tableName}_Status DEFAULT(0);

            -- Add LockedUntil column if missing
            IF COL_LENGTH('[{schemaName}].[{tableName}]', 'LockedUntil') IS NULL
                ALTER TABLE [{schemaName}].[{tableName}] ADD LockedUntil DATETIME2(3) NULL;

            -- Add OwnerToken column if missing
            IF COL_LENGTH('[{schemaName}].[{tableName}]', 'OwnerToken') IS NULL
                ALTER TABLE [{schemaName}].[{tableName}] ADD OwnerToken UNIQUEIDENTIFIER NULL;";

        await connection.ExecuteAsync(addColumnsScript).ConfigureAwait(false);
    }

    private static async Task EnsureWorkQueueIndexAsync(SqlConnection connection, string schemaName, string tableName, string orderingColumn)
    {
        var indexScript = $@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_{tableName}_WorkQueue_Status_{orderingColumn}' AND object_id=OBJECT_ID('[{schemaName}].[{tableName}]'))
                CREATE INDEX IX_{tableName}_WorkQueue_Status_{orderingColumn} ON [{schemaName}].[{tableName}](Status, {orderingColumn})
                INCLUDE(OwnerToken);";

        await connection.ExecuteAsync(indexScript).ConfigureAwait(false);
    }

    private static async Task EnsureScheduledWorkQueueIndexAsync(SqlConnection connection, string schemaName, string tableName, string dueTimeColumn)
    {
        var indexScript = $@"
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_{tableName}_WorkQueue_Status_{dueTimeColumn}' AND object_id=OBJECT_ID('[{schemaName}].[{tableName}]'))
                CREATE INDEX IX_{tableName}_WorkQueue_Status_{dueTimeColumn} ON [{schemaName}].[{tableName}](Status, {dueTimeColumn})
                INCLUDE(OwnerToken);";

        await connection.ExecuteAsync(indexScript).ConfigureAwait(false);
    }

    private static async Task EnsureIdListTvpAsync(SqlConnection connection, string primaryKeyType)
    {
        var tvpName = GetTvpName(primaryKeyType);
        var tvpScript = $@"
            IF TYPE_ID('dbo.{tvpName}') IS NULL
                CREATE TYPE dbo.{tvpName} AS TABLE (Id {primaryKeyType} NOT NULL PRIMARY KEY);";

        await connection.ExecuteAsync(tvpScript).ConfigureAwait(false);
    }

    private static async Task CreateClaimProcedureAsync(SqlConnection connection, string schemaName, string tableName, string primaryKeyColumn, string orderingColumn)
    {
        var procName = $"{tableName}_Claim";
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
              @OwnerToken UNIQUEIDENTIFIER,
              @LeaseSeconds INT,
              @BatchSize INT = 50
            AS
            BEGIN
              SET NOCOUNT ON;
              DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
              DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

              ;WITH cte AS (
                SELECT TOP (@BatchSize) {primaryKeyColumn}
                FROM [{schemaName}].[{tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE Status = {WorkQueueStatus.Ready} /* Ready */
                  AND (LockedUntil IS NULL OR LockedUntil <= @now)
                ORDER BY {orderingColumn}
              )
              UPDATE t
                 SET Status = {WorkQueueStatus.InProgress} /* InProgress */, 
                     OwnerToken = @OwnerToken, 
                     LockedUntil = @until
                OUTPUT inserted.{primaryKeyColumn}
                FROM [{schemaName}].[{tableName}] t
                JOIN cte ON cte.{primaryKeyColumn} = t.{primaryKeyColumn};
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static async Task CreateClaimDueProcedureAsync(SqlConnection connection, string schemaName, string tableName, string primaryKeyColumn, string dueTimeColumn)
    {
        var procName = $"{tableName}_ClaimDue";
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
              @OwnerToken UNIQUEIDENTIFIER,
              @LeaseSeconds INT,
              @BatchSize INT = 50
            AS
            BEGIN
              SET NOCOUNT ON;
              DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
              DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

              ;WITH cte AS (
                SELECT TOP (@BatchSize) {primaryKeyColumn}
                FROM [{schemaName}].[{tableName}] WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE Status = {WorkQueueStatus.Ready} /* Ready */
                  AND {dueTimeColumn} <= @now
                  AND (LockedUntil IS NULL OR LockedUntil <= @now)
                ORDER BY {dueTimeColumn}
              )
              UPDATE t
                 SET Status = {WorkQueueStatus.InProgress} /* InProgress */, 
                     OwnerToken = @OwnerToken, 
                     LockedUntil = @until
                OUTPUT inserted.{primaryKeyColumn}
                FROM [{schemaName}].[{tableName}] t
                JOIN cte ON cte.{primaryKeyColumn} = t.{primaryKeyColumn};
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static async Task CreateAckProcedureAsync(SqlConnection connection, string schemaName, string tableName, string primaryKeyColumn, string primaryKeyType)
    {
        var procName = $"{tableName}_Ack";
        var tvpName = GetTvpName(primaryKeyType);
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
              @OwnerToken UNIQUEIDENTIFIER,
              @Ids dbo.{tvpName} READONLY
            AS
            BEGIN
              SET NOCOUNT ON;
              UPDATE t
                 SET Status = {WorkQueueStatus.Done} /* Done */, 
                     OwnerToken = NULL, 
                     LockedUntil = NULL
                FROM [{schemaName}].[{tableName}] t
                JOIN @Ids i ON i.Id = t.{primaryKeyColumn}
               WHERE t.OwnerToken = @OwnerToken;
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static async Task CreateAbandonProcedureAsync(SqlConnection connection, string schemaName, string tableName, string primaryKeyColumn, string primaryKeyType)
    {
        var procName = $"{tableName}_Abandon";
        var tvpName = GetTvpName(primaryKeyType);
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
              @OwnerToken UNIQUEIDENTIFIER,
              @Ids dbo.{tvpName} READONLY
            AS
            BEGIN
              SET NOCOUNT ON;
              UPDATE t
                 SET Status = {WorkQueueStatus.Ready} /* Ready */, 
                     OwnerToken = NULL, 
                     LockedUntil = NULL
                FROM [{schemaName}].[{tableName}] t
                JOIN @Ids i ON i.Id = t.{primaryKeyColumn}
               WHERE t.OwnerToken = @OwnerToken;
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static async Task CreateFailProcedureAsync(SqlConnection connection, string schemaName, string tableName, string primaryKeyColumn, string primaryKeyType)
    {
        var procName = $"{tableName}_Fail";
        var tvpName = GetTvpName(primaryKeyType);
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
              @OwnerToken UNIQUEIDENTIFIER,
              @Ids dbo.{tvpName} READONLY,
              @ErrorMessage NVARCHAR(MAX) = NULL
            AS
            BEGIN
              SET NOCOUNT ON;
              UPDATE t
                 SET Status = {WorkQueueStatus.Failed} /* Failed */, 
                     OwnerToken = NULL, 
                     LockedUntil = NULL,
                     LastError = @ErrorMessage
                FROM [{schemaName}].[{tableName}] t
                JOIN @Ids i ON i.Id = t.{primaryKeyColumn}
               WHERE t.OwnerToken = @OwnerToken;
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static async Task CreateReapExpiredProcedureAsync(SqlConnection connection, string schemaName, string tableName)
    {
        var procName = $"{tableName}_ReapExpired";
        var procScript = $@"
            CREATE OR ALTER PROCEDURE [{schemaName}].[{procName}]
            AS
            BEGIN
              SET NOCOUNT ON;
              UPDATE [{schemaName}].[{tableName}]
                 SET Status = {WorkQueueStatus.Ready}, 
                     OwnerToken = NULL, 
                     LockedUntil = NULL
               WHERE Status = {WorkQueueStatus.InProgress} /* InProgress */
                 AND LockedUntil IS NOT NULL
                 AND LockedUntil <= SYSUTCDATETIME();
              
              SELECT @@ROWCOUNT;
            END";

        await connection.ExecuteAsync(procScript).ConfigureAwait(false);
    }

    private static string GetTvpName(string primaryKeyType)
    {
        return primaryKeyType.ToUpperInvariant() switch
        {
            "UNIQUEIDENTIFIER" => "UniqueIdentifierIdList",
            "BIGINT" => "BigIntIdList",
            "INT" => "IntIdList",
            _ => "GenericIdList"
        };
    }
}