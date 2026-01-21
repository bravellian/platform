IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'infra')
BEGIN
    EXEC('CREATE SCHEMA [infra]');
END
GO

IF OBJECT_ID(N'infra.Timers', N'U') IS NULL
BEGIN
    CREATE TABLE infra.Timers (
        -- Core scheduling fields
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        DueTime DATETIMEOFFSET NOT NULL,
        Payload NVARCHAR(MAX) NOT NULL,
        Topic NVARCHAR(255) NOT NULL,
        CorrelationId NVARCHAR(255) NULL,

        -- Work queue state management
        StatusCode TINYINT NOT NULL DEFAULT(0),      -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
        LockedUntil DATETIME2(3) NULL,               -- UTC lease expiration time
        OwnerToken UNIQUEIDENTIFIER NULL,            -- Process ownership identifier

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
    WHERE name = 'IX_Timers_WorkQueue'
      AND object_id = OBJECT_ID(N'infra.Timers', N'U'))
BEGIN
    CREATE INDEX IX_Timers_WorkQueue ON infra.Timers(StatusCode, DueTime)
        INCLUDE(Id, OwnerToken)
        WHERE StatusCode = 0;
END
GO
