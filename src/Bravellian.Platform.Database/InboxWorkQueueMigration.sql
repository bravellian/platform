-- Work Queue Migration for Inbox Table
-- Adds required columns and indexes for claim-and-process semantics

-- Create the StringIdList table-valued parameter type if it doesn't exist
IF TYPE_ID('dbo.StringIdList') IS NULL
BEGIN
    CREATE TYPE dbo.StringIdList AS TABLE (
        Id VARCHAR(64) NOT NULL PRIMARY KEY
    );
END
GO

-- Update Inbox table for work queue pattern
IF COL_LENGTH('dbo.Inbox', 'LockedUntil') IS NULL
    ALTER TABLE dbo.Inbox ADD LockedUntil DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.Inbox', 'OwnerToken') IS NULL
    ALTER TABLE dbo.Inbox ADD OwnerToken UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH('dbo.Inbox', 'Topic') IS NULL
    ALTER TABLE dbo.Inbox ADD Topic VARCHAR(128) NULL;
GO

IF COL_LENGTH('dbo.Inbox', 'Payload') IS NULL
    ALTER TABLE dbo.Inbox ADD Payload NVARCHAR(MAX) NULL;
GO

-- Create work queue index for Inbox
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Inbox_WorkQueue' AND object_id=OBJECT_ID('dbo.Inbox'))
BEGIN
    CREATE INDEX IX_Inbox_WorkQueue ON dbo.Inbox(Status, LastSeenUtc)
        INCLUDE(MessageId, OwnerToken) -- Include columns needed for diagnostics
        WHERE Status IN ('Seen', 'Processing'); -- Ready or InProgress states
END
GO

PRINT 'Inbox work queue pattern migration completed successfully.';