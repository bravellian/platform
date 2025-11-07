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

namespace Bravellian.Platform.Tests;

using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

/// <summary>
/// Base test class that provides a SQL Server TestContainer for integration testing.
/// Automatically manages the container lifecycle and database schema setup.
/// </summary>
public abstract class SqlServerTestBase : IAsyncLifetime
{
    private readonly MsSqlContainer msSqlContainer;
    private string? connectionString;

    protected SqlServerTestBase(ITestOutputHelper testOutputHelper)
    {
        this.msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();

        this.TestOutputHelper = testOutputHelper;
    }

    protected ITestOutputHelper TestOutputHelper { get; }

    /// <summary>
    /// Gets the connection string for the running SQL Server container.
    /// Only available after InitializeAsync has been called.
    /// </summary>
    protected string ConnectionString => this.connectionString ?? throw new InvalidOperationException("Container has not been started yet. Make sure InitializeAsync has been called.");

    public virtual async ValueTask InitializeAsync()
    {
        await this.msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        this.connectionString = this.msSqlContainer.GetConnectionString();
        await this.SetupDatabaseSchema().ConfigureAwait(false);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await this.msSqlContainer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            // Create the database schema in the correct order (due to foreign key dependencies)
            await this.ExecuteSqlScript(connection, this.GetOutboxTableScript());
            await this.ExecuteSqlScript(connection, this.GetOutboxStateTableScript());
            await this.ExecuteSqlScript(connection, this.GetInboxTableScript());
            await this.ExecuteSqlScript(connection, this.GetTimersTableScript());
            await this.ExecuteSqlScript(connection, this.GetJobsTableScript());
            await this.ExecuteSqlScript(connection, this.GetJobRunsTableScript());
            await this.ExecuteSqlScript(connection, this.GetSchedulerStateTableScript());

            this.TestOutputHelper.WriteLine($"Database schema created successfully on {connection.DataSource}");
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
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(), -- For backoff strategies

    -- For Idempotency & Tracing
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- A stable ID for the message consumer
    CorrelationId NVARCHAR(255) NULL -- To trace a message through multiple systems
);
GO

-- An index to efficiently query for unprocessed messages, now including the next attempt time.
CREATE INDEX IX_Outbox_GetNext ON dbo.Outbox(IsProcessed, NextAttemptAt)
    INCLUDE(Id, Payload, Topic, RetryCount) -- Include columns needed for processing
    WHERE IsProcessed = 0;
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
    Payload NVARCHAR(MAX) NULL
);
GO

-- Index for efficiently finding messages to process
CREATE INDEX IX_Inbox_Processing ON dbo.Inbox(Status, LastSeenUtc)
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
}
