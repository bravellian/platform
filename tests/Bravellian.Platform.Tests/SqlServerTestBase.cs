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


using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Base test class that provides a SQL Server TestContainer for integration testing.
/// Automatically manages the container lifecycle and database schema setup.
/// When used with the SqlServerCollection, shares a single container across multiple test classes.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer? msSqlContainer;
    private readonly SqlServerCollectionFixture? sharedFixture;
    private string? connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTestBase"/> class with a standalone container.
    /// </summary>
    protected SqlServerTestBase(ITestOutputHelper testOutputHelper)
    {
        msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();

        TestOutputHelper = testOutputHelper;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTestBase"/> class with a shared container.
    /// This constructor is used when the test class is part of the SqlServerCollection.
    /// </summary>
    protected SqlServerTestBase(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture sharedFixture)
    {
        this.sharedFixture = sharedFixture;
        TestOutputHelper = testOutputHelper;
    }

    protected ITestOutputHelper TestOutputHelper { get; }

    /// <summary>
    /// Gets the connection string for the running SQL Server container.
    /// Only available after InitializeAsync has been called.
    /// </summary>
    protected string ConnectionString => connectionString ?? throw new InvalidOperationException("Container has not been started yet. Make sure InitializeAsync has been called.");

    public virtual async ValueTask InitializeAsync()
    {
        if (sharedFixture != null)
        {
            // Using shared container - create a new database in the shared container
            connectionString = await sharedFixture.CreateTestDatabaseAsync("shared").ConfigureAwait(false);
        }
        else
        {
            // Using standalone container
            await msSqlContainer!.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            connectionString = msSqlContainer.GetConnectionString();
        }

        await SetupDatabaseSchema().ConfigureAwait(false);
    }

    public virtual async ValueTask DisposeAsync()
    {
        // Only dispose the container if we own it (standalone mode)
        if (msSqlContainer != null)
        {
            await msSqlContainer.DisposeAsync().ConfigureAwait(false);
        }

        // In shared mode, we don't dispose the container - it's managed by the collection fixture
        // The database will be cleaned up when the container is disposed at the end of all tests
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            // Create table types for work queue stored procedures
            await ExecuteSqlScript(connection, GetTableTypesScript());

            // Create the database schema in the correct order (due to foreign key dependencies)
            await ExecuteSqlScript(connection, GetOutboxTableScript());
            await ExecuteSqlScript(connection, GetOutboxStateTableScript());
            await ExecuteSqlScript(connection, GetInboxTableScript());
            await ExecuteSqlScript(connection, GetTimersTableScript());
            await ExecuteSqlScript(connection, GetJobsTableScript());
            await ExecuteSqlScript(connection, GetJobRunsTableScript());
            await ExecuteSqlScript(connection, GetSchedulerStateTableScript());

            // Create stored procedures
            await ExecuteSqlScript(connection, GetOutboxCleanupProcedure());
            await ExecuteSqlScript(connection, GetInboxCleanupProcedure());
            await ExecuteSqlScript(connection, GetOutboxWorkQueueProcedures());
            await ExecuteSqlScript(connection, GetInboxWorkQueueProcedures());

            TestOutputHelper.WriteLine($"Database schema created successfully on {connection.DataSource}");
        }
    }

    private async Task ExecuteSqlScript(SqlConnection connection, string script)
    {
        // Split by GO statements and execute each batch separately
        var batches = script.Split(
            new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO", "GO\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            var trimmedBatch = batch.Trim();
            if (!string.IsNullOrEmpty(trimmedBatch))
            {
                var command = new SqlCommand(trimmedBatch, connection);
                await using (command.ConfigureAwait(false))
                {
                    await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                }
            }
        }
    }

    private string GetOutboxTableScript()
    {
        return @"
CREATE TABLE dbo.Outbox (
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

    -- For Idempotency & Tracing
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- A stable ID for the message consumer
    CorrelationId NVARCHAR(255) NULL, -- To trace a message through multiple systems

    -- For Delayed Processing
    DueTimeUtc DATETIME2(3) NULL, -- Optional timestamp indicating when the message should become eligible for processing

    -- Work Queue Pattern Columns
    Status TINYINT NOT NULL DEFAULT 0, -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL
);
GO

-- An index to efficiently query for work queue claiming
CREATE INDEX IX_Outbox_WorkQueue ON dbo.Outbox(Status, CreatedAt)
    INCLUDE(Id, LockedUntil, DueTimeUtc)
    WHERE Status = 0;
GO";
    }

    private string GetTimersTableScript()
    {
        return @"
CREATE TABLE dbo.Timers (
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
GO

-- A critical index to find the next due timers efficiently.
CREATE INDEX IX_Timers_GetNext ON dbo.Timers(Status, DueTime)
    INCLUDE(Id, Topic) -- Include columns needed to start processing
    WHERE Status = 'Pending';
GO";
    }

    private string GetJobsTableScript()
    {
        return @"
CREATE TABLE dbo.Jobs (
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
GO

-- Unique index to prevent duplicate job definitions
CREATE UNIQUE INDEX UQ_Jobs_JobName ON dbo.Jobs(JobName);
GO";
    }

    private string GetInboxTableScript()
    {
        return @"
CREATE TABLE dbo.Inbox (
    -- Core Fields
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,
    FirstSeenUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastSeenUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedUtc DATETIME2(3) NULL,
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen', -- Seen, Processing, Done, Dead
    
    -- Optional work queue columns (for advanced scenarios)
    Topic VARCHAR(128) NULL,
    Payload NVARCHAR(MAX) NULL,
    
    -- For Delayed Processing
    DueTimeUtc DATETIME2(3) NULL, -- Optional timestamp indicating when the message should become eligible for processing

    -- Work Queue Pattern Columns
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL
);
GO

-- Index for efficiently finding messages to process
CREATE INDEX IX_Inbox_Processing ON dbo.Inbox(Status, LastSeenUtc)
    WHERE Status IN ('Seen', 'Processing');
GO

-- Work queue index for claiming messages
CREATE INDEX IX_Inbox_WorkQueue ON dbo.Inbox(Status, LastSeenUtc)
    INCLUDE(MessageId, OwnerToken)
    WHERE Status IN ('Seen', 'Processing');
GO";
    }

    private string GetOutboxStateTableScript()
    {
        return @"
CREATE TABLE dbo.OutboxState (
    Id INT NOT NULL CONSTRAINT PK_OutboxState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastDispatchAt DATETIME2(3) NULL
);
GO

-- Insert initial state row
INSERT dbo.OutboxState (Id, CurrentFencingToken, LastDispatchAt) 
VALUES (1, 0, NULL);
GO";
    }

    private string GetSchedulerStateTableScript()
    {
        return @"
CREATE TABLE dbo.SchedulerState (
    Id INT NOT NULL CONSTRAINT PK_SchedulerState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastRunAt DATETIME2(3) NULL
);
GO

-- Insert initial state row
INSERT dbo.SchedulerState (Id, CurrentFencingToken, LastRunAt) 
VALUES (1, 0, NULL);
GO";
    }

    private string GetJobRunsTableScript()
    {
        return @"
CREATE TABLE dbo.JobRuns (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Jobs(Id),
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
GO

-- Index to find pending job runs that are due
CREATE INDEX IX_JobRuns_GetNext ON dbo.JobRuns(Status, ScheduledTime)
    WHERE Status = 'Pending';
GO";
    }

    private string GetOutboxCleanupProcedure()
    {
        return @"
CREATE OR ALTER PROCEDURE dbo.Outbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());
    
    DELETE FROM dbo.Outbox
     WHERE IsProcessed = 1
       AND ProcessedAt IS NOT NULL
       AND ProcessedAt < @cutoffTime;
       
    SELECT @@ROWCOUNT AS DeletedCount;
END
GO";
    }

    private string GetInboxCleanupProcedure()
    {
        return @"
CREATE OR ALTER PROCEDURE dbo.Inbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIME2(3) = DATEADD(SECOND, -@RetentionSeconds, SYSUTCDATETIME());
    
    DELETE FROM dbo.Inbox
     WHERE Status = 'Done'
       AND ProcessedUtc IS NOT NULL
       AND ProcessedUtc < @cutoffTime;
       
    SELECT @@ROWCOUNT AS DeletedCount;
END
GO";
    }

    private string GetTableTypesScript()
    {
        return @"
-- Create GuidIdList type for Outbox stored procedures
IF TYPE_ID('dbo.GuidIdList') IS NULL
BEGIN
    CREATE TYPE dbo.GuidIdList AS TABLE (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    );
END
GO

-- Create StringIdList type for Inbox stored procedures
IF TYPE_ID('dbo.StringIdList') IS NULL
BEGIN
    CREATE TYPE dbo.StringIdList AS TABLE (
        Id VARCHAR(64) NOT NULL PRIMARY KEY
    );
END
GO";
    }

    private string GetOutboxWorkQueueProcedures()
    {
        return @"
CREATE OR ALTER PROCEDURE dbo.Outbox_Claim
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
        WHERE Status = 0 
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
    OUTPUT inserted.Id
    FROM dbo.Outbox o JOIN cte ON cte.Id = o.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET Status = 3, OwnerToken = NULL, LockedUntil = NULL
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Outbox SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
    WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO";
    }

    private string GetInboxWorkQueueProcedures()
    {
        return @"
CREATE OR ALTER PROCEDURE dbo.Inbox_Claim
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
        WHERE Status IN ('Seen', 'Processing') 
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY LastSeenUtc
    )
    UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
    OUTPUT inserted.MessageId
    FROM dbo.Inbox i JOIN cte ON cte.MessageId = i.MessageId;
END
GO

CREATE OR ALTER PROCEDURE dbo.Inbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL, ProcessedUtc = SYSUTCDATETIME(), LastSeenUtc = SYSUTCDATETIME()
    FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE dbo.Inbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE dbo.Inbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY,
    @Reason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Dead', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    FROM dbo.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE dbo.Inbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Inbox SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO";
    }
}
