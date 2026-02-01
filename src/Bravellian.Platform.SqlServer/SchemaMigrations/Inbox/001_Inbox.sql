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

IF OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'MessageId') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD MessageId VARCHAR(64) NOT NULL DEFAULT '';
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Source') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Source VARCHAR(64) NOT NULL DEFAULT '';
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Hash') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Hash BINARY(32) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'FirstSeenUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD FirstSeenUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'LastSeenUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD LastSeenUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'ProcessedUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD ProcessedUtc DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'DueTimeUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD DueTimeUtc DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Attempts') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Attempts INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Status VARCHAR(16) NOT NULL DEFAULT 'Seen';
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'LastError') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD LastError NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD LockedUntil DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'OwnerToken') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD OwnerToken UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Topic') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Topic VARCHAR(128) NULL;
    IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'Payload') IS NULL
        ALTER TABLE [$SchemaName$].[$InboxTable$] ADD Payload NVARCHAR(MAX) NULL;
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
