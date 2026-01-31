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


using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Base test class that provides a SQL Server TestContainer for integration testing.
/// Automatically manages the container lifecycle and database schema setup.
/// When used with the SqlServerCollection, shares a single container across multiple test classes.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime
{
    private const string SaPassword = "Str0ng!Passw0rd!";
    private readonly IContainer? msSqlContainer;
    private readonly SqlServerCollectionFixture? sharedFixture;
    private string? connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTestBase"/> class with a standalone container.
    /// </summary>
    protected SqlServerTestBase(ITestOutputHelper testOutputHelper)
    {
        msSqlContainer = new ContainerBuilder("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SaPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(1433, true)
            .WithReuse(true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1433))
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
            sharedFixture.EnsureAvailable();
            // Using shared container - create a new database in the shared container
            connectionString = await sharedFixture.CreateTestDatabaseAsync("shared").ConfigureAwait(false);
        }
        else
        {
            // Using standalone container
            await msSqlContainer!.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            connectionString = BuildConnectionString(msSqlContainer);
            await WaitForServerReadyAsync(connectionString, TestContext.Current.CancellationToken).ConfigureAwait(false);
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
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            await ExecuteSqlScript(connection, GetCreateSchemaScript()).ConfigureAwait(false);

            // Create table types for work queue stored procedures
            await ExecuteSqlScript(connection, GetTableTypesScript()).ConfigureAwait(false);

            // Create the database schema in the correct order (due to foreign key dependencies)
            await ExecuteSqlScript(connection, GetOutboxTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetOutboxStateTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetInboxTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetTimersTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetJobsTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetJobRunsTableScript()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetSchedulerStateTableScript()).ConfigureAwait(false);

            // Create stored procedures
            await ExecuteSqlScript(connection, GetOutboxCleanupProcedure()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetInboxCleanupProcedure()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetOutboxWorkQueueProcedures()).ConfigureAwait(false);
            await ExecuteSqlScript(connection, GetInboxWorkQueueProcedures()).ConfigureAwait(false);

            TestOutputHelper.WriteLine($"Database schema created successfully on {connection.DataSource}");
        }
    }

    private static string GetCreateSchemaScript()
    {
        return """
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'infra')
            BEGIN
                EXEC('CREATE SCHEMA [infra]')
            END
            """;
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
                    await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private string GetOutboxTableScript()
    {
        return @"
CREATE TABLE infra.Outbox (
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
CREATE INDEX IX_Outbox_WorkQueue ON infra.Outbox(Status, CreatedAt)
    INCLUDE(Id, LockedUntil, DueTimeUtc)
    WHERE Status = 0;
GO";
    }

    private string GetTimersTableScript()
    {
        return @"
CREATE TABLE infra.Timers (
    -- Core Fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    DueTime DATETIMEOFFSET NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,

    -- For tracing back to business logic
    CorrelationId NVARCHAR(255) NULL,

    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL,

    -- Legacy status fields
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

-- Work queue index for claiming due timers efficiently.
CREATE INDEX IX_Timers_WorkQueue ON infra.Timers(StatusCode, DueTime)
    INCLUDE(Id, OwnerToken)
    WHERE StatusCode = 0;
GO";
    }

    private string GetJobsTableScript()
    {
        return @"
CREATE TABLE infra.Jobs (
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
CREATE UNIQUE INDEX UQ_Jobs_JobName ON infra.Jobs(JobName);
GO";
    }

    private string GetInboxTableScript()
    {
        return @"
CREATE TABLE infra.Inbox (
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

-- Work queue index for claiming messages
CREATE INDEX IX_Inbox_WorkQueue ON infra.Inbox(Status, LastSeenUtc)
    INCLUDE(MessageId, OwnerToken)
    WHERE Status IN ('Seen', 'Processing');
GO";
    }

    private string GetOutboxStateTableScript()
    {
        return @"
CREATE TABLE infra.OutboxState (
    Id INT NOT NULL CONSTRAINT PK_OutboxState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastDispatchAt DATETIME2(3) NULL
);
GO

-- Insert initial state row
INSERT infra.OutboxState (Id, CurrentFencingToken, LastDispatchAt)
VALUES (1, 0, NULL);
GO";
    }

    private string GetSchedulerStateTableScript()
    {
        return @"
CREATE TABLE infra.SchedulerState (
    Id INT NOT NULL CONSTRAINT PK_SchedulerState PRIMARY KEY,
    CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
    LastRunAt DATETIME2(3) NULL
);
GO

-- Insert initial state row
INSERT infra.SchedulerState (Id, CurrentFencingToken, LastRunAt)
VALUES (1, 0, NULL);
GO";
    }

    private string GetJobRunsTableScript()
    {
        return @"
CREATE TABLE infra.JobRuns (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    JobId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES infra.Jobs(Id),
    ScheduledTime DATETIMEOFFSET NOT NULL,

    -- Work queue state management
    StatusCode TINYINT NOT NULL DEFAULT(0),
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL,

    -- Legacy status fields
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

-- Work queue index for claiming due job runs efficiently.
CREATE INDEX IX_JobRuns_WorkQueue ON infra.JobRuns(StatusCode, ScheduledTime)
    INCLUDE(Id, OwnerToken)
    WHERE StatusCode = 0;
GO";
    }

    private string GetOutboxCleanupProcedure()
    {
        return @"
CREATE OR ALTER PROCEDURE infra.Outbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

    DELETE FROM infra.Outbox
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
CREATE OR ALTER PROCEDURE infra.Inbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIME2(3) = DATEADD(SECOND, -@RetentionSeconds, SYSUTCDATETIME());

    DELETE FROM infra.Inbox
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
IF TYPE_ID('infra.GuidIdList') IS NULL
BEGIN
    CREATE TYPE infra.GuidIdList AS TABLE (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    );
END
GO

-- Create StringIdList type for Inbox stored procedures
IF TYPE_ID('infra.StringIdList') IS NULL
BEGIN
    CREATE TYPE infra.StringIdList AS TABLE (
        Id VARCHAR(64) NOT NULL PRIMARY KEY
    );
END
GO";
    }

    private string GetOutboxWorkQueueProcedures()
    {
        return @"
CREATE OR ALTER PROCEDURE infra.Outbox_Claim
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
        FROM infra.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
    OUTPUT inserted.Id
    FROM infra.Outbox o JOIN cte ON cte.Id = o.Id;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @DueTimeUtc DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    UPDATE o SET
        Status = 0,
        OwnerToken = NULL,
        LockedUntil = NULL,
        RetryCount = RetryCount + 1,
        LastError = ISNULL(@LastError, o.LastError),
        DueTimeUtc = COALESCE(@DueTimeUtc, o.DueTimeUtc, @now)
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @ProcessedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET
        Status = 3,
        OwnerToken = NULL,
        LockedUntil = NULL,
        LastError = ISNULL(@LastError, o.LastError),
        ProcessedBy = ISNULL(@ProcessedBy, o.ProcessedBy)
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE infra.Outbox SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
    WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO";
    }

    private string GetInboxWorkQueueProcedures()
    {
        return @"
CREATE OR ALTER PROCEDURE infra.Inbox_Claim
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
        FROM infra.Inbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status IN ('Seen', 'Processing')
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY LastSeenUtc
    )
    UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
    OUTPUT inserted.MessageId
    FROM infra.Inbox i JOIN cte ON cte.MessageId = i.MessageId;
END
GO

CREATE OR ALTER PROCEDURE infra.Inbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL, ProcessedUtc = SYSUTCDATETIME(), LastSeenUtc = SYSUTCDATETIME()
    FROM infra.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE infra.Inbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    FROM infra.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE infra.Inbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.StringIdList READONLY,
    @Reason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Dead', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    FROM infra.Inbox i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE infra.Inbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE infra.Inbox SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO";
    }

    private static string BuildConnectionString(IContainer container)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{container.Hostname},{container.GetMappedPublicPort(1433)}",
            UserID = "sa",
            Password = SaPassword,
            InitialCatalog = "master",
            Encrypt = false,
            TrustServerCertificate = true,
        };

        return builder.ConnectionString;
    }

    private static async Task WaitForServerReadyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                var connection = new SqlConnection(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (SqlException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("SQL Server did not become available before the timeout.");
    }
}

