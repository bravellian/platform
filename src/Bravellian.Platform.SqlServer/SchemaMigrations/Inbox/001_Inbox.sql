IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF TYPE_ID(N'[$SchemaName$].StringIdList') IS NULL
BEGIN
    CREATE TYPE [$SchemaName$].StringIdList AS TABLE (Id VARCHAR(64) NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$InboxTable$] (
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

IF COL_LENGTH(N'[$SchemaName$].[$InboxTable$]', 'LastError') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$]
        ADD LastError NVARCHAR(MAX) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_$InboxTable$_ProcessedUtc' AND object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$InboxTable$_ProcessedUtc ON [$SchemaName$].[$InboxTable$](ProcessedUtc)
        WHERE ProcessedUtc IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_$InboxTable$_Status' AND object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$InboxTable$_Status ON [$SchemaName$].[$InboxTable$](Status);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_$InboxTable$_Status_ProcessedUtc' AND object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$InboxTable$_Status_ProcessedUtc ON [$SchemaName$].[$InboxTable$](Status, ProcessedUtc)
        WHERE Status = 'Done' AND ProcessedUtc IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_$InboxTable$_WorkQueue' AND object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$InboxTable$_WorkQueue ON [$SchemaName$].[$InboxTable$](Status, LastSeenUtc)
        INCLUDE(MessageId, OwnerToken)
        WHERE Status IN ('Seen', 'Processing');
END
GO
