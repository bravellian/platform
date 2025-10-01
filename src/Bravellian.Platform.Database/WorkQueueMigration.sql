-- Generic Work Queue Pattern Migration Script
-- Adds required columns and indexes for claim-and-process semantics

-- Create the BigIntIdList table-valued parameter type if it doesn't exist
IF TYPE_ID('dbo.BigIntIdList') IS NULL
BEGIN
    CREATE TYPE dbo.BigIntIdList AS TABLE (
        Id BIGINT NOT NULL PRIMARY KEY
    );
END
GO

-- Create the GuidIdList table-valued parameter type if it doesn't exist
IF TYPE_ID('dbo.GuidIdList') IS NULL
BEGIN
    CREATE TYPE dbo.GuidIdList AS TABLE (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    );
END
GO

-- Update Outbox table for work queue pattern
IF COL_LENGTH('dbo.Outbox', 'Status') IS NULL
    ALTER TABLE dbo.Outbox ADD Status TINYINT NOT NULL CONSTRAINT DF_Outbox_Status DEFAULT(0);
GO

IF COL_LENGTH('dbo.Outbox', 'LockedUntil') IS NULL
    ALTER TABLE dbo.Outbox ADD LockedUntil DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.Outbox', 'OwnerToken') IS NULL
    ALTER TABLE dbo.Outbox ADD OwnerToken UNIQUEIDENTIFIER NULL;
GO

-- Update the existing index to support work queue operations
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Outbox_GetNext' AND object_id=OBJECT_ID('dbo.Outbox'))
    DROP INDEX IX_Outbox_GetNext ON dbo.Outbox;
GO

CREATE INDEX IX_Outbox_WorkQueue ON dbo.Outbox(Status, CreatedAt)
    INCLUDE(Id, OwnerToken) -- Include columns needed for diagnostics
    WHERE Status IN (0, 1); -- Ready or InProgress
GO

-- Update Timers table for work queue pattern  
IF COL_LENGTH('dbo.Timers', 'Status') IS NOT NULL AND COL_LENGTH('dbo.Timers', 'StatusCode') IS NULL
BEGIN
    -- Convert existing string Status to TINYINT StatusCode
    ALTER TABLE dbo.Timers ADD StatusCode TINYINT NOT NULL CONSTRAINT DF_Timers_StatusCode DEFAULT(0);
    
    UPDATE dbo.Timers SET StatusCode = 
        CASE Status
            WHEN 'Pending' THEN 0
            WHEN 'Claimed' THEN 1
            WHEN 'Processed' THEN 2
            WHEN 'Failed' THEN 3
            ELSE 0
        END;
END
GO

IF COL_LENGTH('dbo.Timers', 'LockedUntil') IS NULL
    ALTER TABLE dbo.Timers ADD LockedUntil DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.Timers', 'OwnerToken') IS NULL
    ALTER TABLE dbo.Timers ADD OwnerToken UNIQUEIDENTIFIER NULL;
GO

-- Update the existing index to support work queue operations
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Timers_GetNext' AND object_id=OBJECT_ID('dbo.Timers'))
    DROP INDEX IX_Timers_GetNext ON dbo.Timers;
GO

CREATE INDEX IX_Timers_WorkQueue ON dbo.Timers(StatusCode, DueTime)
    INCLUDE(Id, OwnerToken) -- Include columns needed for diagnostics
    WHERE StatusCode IN (0, 1); -- Ready or InProgress
GO

-- Update JobRuns table for work queue pattern
IF COL_LENGTH('dbo.JobRuns', 'Status') IS NOT NULL AND COL_LENGTH('dbo.JobRuns', 'StatusCode') IS NULL
BEGIN
    -- Convert existing string Status to TINYINT StatusCode
    ALTER TABLE dbo.JobRuns ADD StatusCode TINYINT NOT NULL CONSTRAINT DF_JobRuns_StatusCode DEFAULT(0);
    
    UPDATE dbo.JobRuns SET StatusCode = 
        CASE Status
            WHEN 'Pending' THEN 0
            WHEN 'Claimed' THEN 1
            WHEN 'Running' THEN 1
            WHEN 'Succeeded' THEN 2
            WHEN 'Failed' THEN 3
            ELSE 0
        END;
END
GO

IF COL_LENGTH('dbo.JobRuns', 'LockedUntil') IS NULL
    ALTER TABLE dbo.JobRuns ADD LockedUntil DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.JobRuns', 'OwnerToken') IS NULL
    ALTER TABLE dbo.JobRuns ADD OwnerToken UNIQUEIDENTIFIER NULL;
GO

-- Update the existing index to support work queue operations
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_JobRuns_GetNext' AND object_id=OBJECT_ID('dbo.JobRuns'))
    DROP INDEX IX_JobRuns_GetNext ON dbo.JobRuns;
GO

CREATE INDEX IX_JobRuns_WorkQueue ON dbo.JobRuns(StatusCode, ScheduledTime)
    INCLUDE(Id, OwnerToken) -- Include columns needed for diagnostics
    WHERE StatusCode IN (0, 1); -- Ready or InProgress
GO

PRINT 'Work queue pattern migration completed successfully.';