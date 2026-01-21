IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF TYPE_ID(N'[$SchemaName$].GuidIdList') IS NULL
BEGIN
    CREATE TYPE [$SchemaName$].GuidIdList AS TABLE (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$JobsTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$JobsTable$] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        JobName NVARCHAR(100) NOT NULL,
        CronSchedule NVARCHAR(100) NOT NULL,
        Topic NVARCHAR(255) NOT NULL,
        Payload NVARCHAR(MAX) NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,

        -- State tracking for the scheduler
        NextDueTime DATETIMEOFFSET NULL,
        LastRunTime DATETIMEOFFSET NULL,
        LastRunStatus NVARCHAR(20) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'UQ_$JobsTable$_JobName' AND object_id = OBJECT_ID(N'[$SchemaName$].[$JobsTable$]', N'U'))
BEGIN
    CREATE UNIQUE INDEX UQ_$JobsTable$_JobName ON [$SchemaName$].[$JobsTable$](JobName);
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$JobRunsTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$JobRunsTable$] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        JobId UNIQUEIDENTIFIER NOT NULL REFERENCES [$SchemaName$].[$JobsTable$](Id),
        ScheduledTime DATETIMEOFFSET NOT NULL,

        -- Work queue state management
        StatusCode TINYINT NOT NULL DEFAULT(0),
        LockedUntil DATETIME2(3) NULL,
        OwnerToken UNIQUEIDENTIFIER NULL,

        -- Legacy fields for compatibility
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        ClaimedBy NVARCHAR(100) NULL,
        ClaimedAt DATETIMEOFFSET NULL,
        RetryCount INT NOT NULL DEFAULT 0,

        -- Execution tracking
        StartTime DATETIMEOFFSET NULL,
        EndTime DATETIMEOFFSET NULL,
        Output NVARCHAR(MAX) NULL,
        LastError NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_$JobRunsTable$_WorkQueue'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$JobRunsTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$JobRunsTable$_WorkQueue ON [$SchemaName$].[$JobRunsTable$](StatusCode, ScheduledTime)
        INCLUDE(Id, OwnerToken)
        WHERE StatusCode = 0;
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$TimersTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$TimersTable$] (
        -- Core scheduling fields
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        DueTime DATETIMEOFFSET NOT NULL,
        Payload NVARCHAR(MAX) NOT NULL,
        Topic NVARCHAR(255) NOT NULL,
        CorrelationId NVARCHAR(255) NULL,

        -- Work queue state management
        StatusCode TINYINT NOT NULL DEFAULT(0),
        LockedUntil DATETIME2(3) NULL,
        OwnerToken UNIQUEIDENTIFIER NULL,

        -- Legacy status field (for compatibility)
        Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        ClaimedBy NVARCHAR(100) NULL,
        ClaimedAt DATETIMEOFFSET NULL,
        RetryCount INT NOT NULL DEFAULT 0,

        -- Auditing
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        ProcessedAt DATETIMEOFFSET NULL,
        LastError NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_$TimersTable$_WorkQueue'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$TimersTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$TimersTable$_WorkQueue ON [$SchemaName$].[$TimersTable$](StatusCode, DueTime)
        INCLUDE(Id, OwnerToken)
        WHERE StatusCode = 0;
END
GO

IF OBJECT_ID(N'[$SchemaName$].SchedulerState', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].SchedulerState (
        Id INT NOT NULL CONSTRAINT PK_SchedulerState PRIMARY KEY,
        CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
        LastRunAt DATETIMEOFFSET(3) NULL
    );

    INSERT [$SchemaName$].SchedulerState (Id, CurrentFencingToken, LastRunAt)
    VALUES (1, 0, NULL);
END
GO
