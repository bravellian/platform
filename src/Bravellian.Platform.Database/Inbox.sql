IF TYPE_ID(N'dbo.StringIdList') IS NULL
BEGIN
    CREATE TYPE dbo.StringIdList AS TABLE (Id VARCHAR(64) NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'dbo.Inbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Inbox (
        -- Core identification
        MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
        Source VARCHAR(64) NOT NULL,
        Hash BINARY(32) NULL,

        -- Timing tracking
        FirstSeenUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        LastSeenUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        ProcessedUtc DATETIMEOFFSET(3) NULL,
        DueTimeUtc DATETIMEOFFSET(3) NULL,

        -- Processing status
        Attempts INT NOT NULL DEFAULT 0,
        Status VARCHAR(16) NOT NULL DEFAULT 'Seen'
            CONSTRAINT CK_Inbox_Status CHECK (Status IN ('Seen', 'Processing', 'Done', 'Dead')),
        LastError NVARCHAR(MAX) NULL,

        -- Work Queue Pattern Columns
        LockedUntil DATETIMEOFFSET(3) NULL,
        OwnerToken UNIQUEIDENTIFIER NULL,
        Topic VARCHAR(128) NULL,
        Payload NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_Inbox_ProcessedUtc' AND object_id = OBJECT_ID(N'dbo.Inbox', N'U'))
BEGIN
    CREATE INDEX IX_Inbox_ProcessedUtc ON dbo.Inbox(ProcessedUtc)
        WHERE ProcessedUtc IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_Inbox_Status' AND object_id = OBJECT_ID(N'dbo.Inbox', N'U'))
BEGIN
    CREATE INDEX IX_Inbox_Status ON dbo.Inbox(Status);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_Inbox_Status_ProcessedUtc' AND object_id = OBJECT_ID(N'dbo.Inbox', N'U'))
BEGIN
    CREATE INDEX IX_Inbox_Status_ProcessedUtc ON dbo.Inbox(Status, ProcessedUtc)
        WHERE Status = 'Done' AND ProcessedUtc IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_Inbox_WorkQueue' AND object_id = OBJECT_ID(N'dbo.Inbox', N'U'))
BEGIN
    CREATE INDEX IX_Inbox_WorkQueue ON dbo.Inbox(Status, LastSeenUtc)
        INCLUDE(MessageId, OwnerToken)
        WHERE Status IN ('Seen', 'Processing');
END
GO
