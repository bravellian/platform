IF OBJECT_ID(N'dbo.Jobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Jobs (
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
    SELECT 1 FROM sys.indexes WHERE name = 'UQ_Jobs_JobName' AND object_id = OBJECT_ID(N'dbo.Jobs', N'U'))
BEGIN
    CREATE UNIQUE INDEX UQ_Jobs_JobName ON dbo.Jobs(JobName);
END
GO
