CREATE TABLE dbo.Inbox (
    -- Core identification
    MessageId VARCHAR(64) NOT NULL PRIMARY KEY,
    Source VARCHAR(64) NOT NULL,
    Hash BINARY(32) NULL,
    
    -- Timing tracking
    FirstSeenUtc DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    LastSeenUtc DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    ProcessedUtc DATETIME2(3) NULL,
    
    -- Processing status
    Attempts INT NOT NULL DEFAULT 0,
    Status VARCHAR(16) NOT NULL DEFAULT 'Seen'
        CONSTRAINT CK_Inbox_Status CHECK (Status IN ('Seen', 'Processing', 'Done', 'Dead'))
);
GO

-- Index for querying processed messages efficiently
CREATE INDEX IX_Inbox_ProcessedUtc ON dbo.Inbox(ProcessedUtc)
    WHERE ProcessedUtc IS NOT NULL;
GO

-- Index for querying by status
CREATE INDEX IX_Inbox_Status ON dbo.Inbox(Status);
GO

-- Index for efficient cleanup of old processed messages
CREATE INDEX IX_Inbox_Status_ProcessedUtc ON dbo.Inbox(Status, ProcessedUtc)
    WHERE Status = 'Done' AND ProcessedUtc IS NOT NULL;
GO