IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
BEGIN
    BEGIN TRY
        DECLARE @schemaSql NVARCHAR(4000) = N'CREATE SCHEMA ' + QUOTENAME('$SchemaName$');
        EXEC sp_executesql @schemaSql;
    END TRY
    BEGIN CATCH
        IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
        BEGIN
            THROW;
        END
    END CATCH
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.types t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE t.is_table_type = 1
      AND LOWER(s.name) = LOWER('$SchemaName$')
      AND LOWER(t.name) = LOWER('GuidIdList'))
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

IF OBJECT_ID(N'[$SchemaName$].[$JobsTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'JobName') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD JobName NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'CronSchedule') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD CronSchedule NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'Topic') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD Topic NVARCHAR(255) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'Payload') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD Payload NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'IsEnabled') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD IsEnabled BIT NOT NULL DEFAULT 1;
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'NextDueTime') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD NextDueTime DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'LastRunTime') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD LastRunTime DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobsTable$]', 'LastRunStatus') IS NULL
        ALTER TABLE [$SchemaName$].[$JobsTable$] ADD LastRunStatus NVARCHAR(20) NULL;
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

IF OBJECT_ID(N'[$SchemaName$].[$JobRunsTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'JobId') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD JobId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'ScheduledTime') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD ScheduledTime DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'StatusCode') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD StatusCode TINYINT NOT NULL DEFAULT(0);
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD LockedUntil DATETIME2(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'OwnerToken') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD OwnerToken UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD Status NVARCHAR(20) NOT NULL DEFAULT 'Pending';
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'ClaimedBy') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD ClaimedBy NVARCHAR(100) NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'ClaimedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD ClaimedAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'RetryCount') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD RetryCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'StartTime') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD StartTime DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'EndTime') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD EndTime DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'Output') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD Output NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$JobRunsTable$]', 'LastError') IS NULL
        ALTER TABLE [$SchemaName$].[$JobRunsTable$] ADD LastError NVARCHAR(MAX) NULL;
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

IF OBJECT_ID(N'[$SchemaName$].[$TimersTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'DueTime') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD DueTime DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'Payload') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD Payload NVARCHAR(MAX) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'Topic') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD Topic NVARCHAR(255) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'CorrelationId') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD CorrelationId NVARCHAR(255) NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'StatusCode') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD StatusCode TINYINT NOT NULL DEFAULT(0);
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD LockedUntil DATETIME2(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'OwnerToken') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD OwnerToken UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD Status NVARCHAR(20) NOT NULL DEFAULT 'Pending';
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'ClaimedBy') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD ClaimedBy NVARCHAR(100) NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'ClaimedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD ClaimedAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'RetryCount') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD RetryCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'ProcessedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD ProcessedAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$TimersTable$]', 'LastError') IS NULL
        ALTER TABLE [$SchemaName$].[$TimersTable$] ADD LastError NVARCHAR(MAX) NULL;
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

IF OBJECT_ID(N'[$SchemaName$].SchedulerState', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].SchedulerState', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].SchedulerState ADD Id INT NOT NULL DEFAULT 1;
    IF COL_LENGTH('[$SchemaName$].SchedulerState', 'CurrentFencingToken') IS NULL
        ALTER TABLE [$SchemaName$].SchedulerState ADD CurrentFencingToken BIGINT NOT NULL DEFAULT(0);
    IF COL_LENGTH('[$SchemaName$].SchedulerState', 'LastRunAt') IS NULL
        ALTER TABLE [$SchemaName$].SchedulerState ADD LastRunAt DATETIMEOFFSET(3) NULL;
END
GO
