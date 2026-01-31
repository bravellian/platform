IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'infra')
BEGIN
    EXEC('CREATE SCHEMA [infra]');
END
GO

IF OBJECT_ID(N'infra.JobRuns', N'U') IS NULL
BEGIN
    CREATE TABLE infra.JobRuns (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        JobId UNIQUEIDENTIFIER NOT NULL REFERENCES infra.Jobs(Id),
        ScheduledTime DATETIMEOFFSET NOT NULL,

        -- Work queue state management
        StatusCode TINYINT NOT NULL DEFAULT(0),      -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
        LockedUntil DATETIME2(3) NULL,               -- UTC lease expiration time
        OwnerToken UNIQUEIDENTIFIER NULL,            -- Process ownership identifier

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
    WHERE name = 'IX_JobRuns_WorkQueue'
      AND object_id = OBJECT_ID(N'infra.JobRuns', N'U'))
BEGIN
    CREATE INDEX IX_JobRuns_WorkQueue ON infra.JobRuns(StatusCode, ScheduledTime)
        INCLUDE(Id, OwnerToken)
        WHERE StatusCode = 0;
END
GO
