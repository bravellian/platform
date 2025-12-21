IF TYPE_ID(N'dbo.GuidIdList') IS NULL
BEGIN
    CREATE TYPE dbo.GuidIdList AS TABLE (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'dbo.Outbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Outbox (
        -- Core Fields
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Payload NVARCHAR(MAX) NOT NULL,
        Topic NVARCHAR(255) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

        -- Processing Status & Auditing
        IsProcessed BIT NOT NULL DEFAULT 0,
        ProcessedAt DATETIMEOFFSET NULL,
        ProcessedBy NVARCHAR(100) NULL,

        -- For Robustness & Error Handling
        RetryCount INT NOT NULL DEFAULT 0,
        LastError NVARCHAR(MAX) NULL,

        -- For Idempotency & Tracing
        MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        CorrelationId NVARCHAR(255) NULL,

        -- For Delayed Processing
        DueTimeUtc DATETIMEOFFSET(3) NULL,

        -- Work Queue Pattern Columns
        Status TINYINT NOT NULL DEFAULT 0, -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
        LockedUntil DATETIMEOFFSET(3) NULL,
        OwnerToken UNIQUEIDENTIFIER NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Outbox_WorkQueue'
      AND object_id = OBJECT_ID(N'dbo.Outbox', N'U'))
BEGIN
    CREATE INDEX IX_Outbox_WorkQueue ON dbo.Outbox(Status, CreatedAt)
        INCLUDE(Id, LockedUntil, DueTimeUtc)
        WHERE Status = 0;
END
GO

IF OBJECT_ID(N'dbo.OutboxState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutboxState (
        Id INT NOT NULL CONSTRAINT PK_OutboxState PRIMARY KEY,
        CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
        LastDispatchAt DATETIMEOFFSET(3) NULL
    );
END
GO
