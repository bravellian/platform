CREATE TABLE dbo.Outbox (
    -- Core Fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- Processing Status & Auditing (Your suggestions)
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL, -- e.g., machine name or instance ID

    -- For Robustness & Error Handling
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(), -- For backoff strategies

    -- For Idempotency & Tracing
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- A stable ID for the message consumer
    CorrelationId UNIQUEIDENTIFIER NULL -- To trace a message through multiple systems
);
GO

-- An index to efficiently query for unprocessed messages, now including the next attempt time.
CREATE INDEX IX_Outbox_GetNext ON dbo.Outbox(IsProcessed, NextAttemptAt)
    INCLUDE(Id, Payload, Topic, RetryCount) -- Include columns needed for processing
    WHERE IsProcessed = 0;
GO