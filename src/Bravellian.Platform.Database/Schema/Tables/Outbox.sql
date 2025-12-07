/*
 * Outbox Table - Transactional Outbox Pattern
 * 
 * Purpose: Stores outbound messages for reliable publishing
 * Pattern: Work Queue with claim-ack-abandon semantics
 * 
 * Key Features:
 * - Atomic claim using READPAST, UPDLOCK hints
 * - Lease-based processing with LockedUntil
 * - Status tracking: 0=Ready, 1=InProgress, 2=Done, 3=Failed
 * - Retry support with exponential backoff via DueTimeUtc
 * 
 * Dependencies: GuidIdList type
 * Related: Outbox_Claim, Outbox_Ack, Outbox_Abandon, Outbox_Fail
 */

CREATE TABLE [dbo].[Outbox]
(
    -- Core Fields
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [Payload] NVARCHAR(MAX) NOT NULL,
    [Topic] NVARCHAR(255) NOT NULL,
    [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- Processing Status & Auditing
    [IsProcessed] BIT NOT NULL DEFAULT 0,
    [ProcessedAt] DATETIMEOFFSET NULL,
    [ProcessedBy] NVARCHAR(100) NULL,

    -- Error Handling & Retry
    [RetryCount] INT NOT NULL DEFAULT 0,
    [LastError] NVARCHAR(MAX) NULL,

    -- Idempotency & Tracing
    [MessageId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [CorrelationId] NVARCHAR(255) NULL,

    -- Delayed Processing
    [DueTimeUtc] DATETIME2(3) NULL,

    -- Work Queue Pattern Columns
    [Status] TINYINT NOT NULL DEFAULT 0, -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    [LockedUntil] DATETIME2(3) NULL,
    [OwnerToken] UNIQUEIDENTIFIER NULL
);
GO

-- Work Queue Index: Optimized for atomic claims
CREATE INDEX [IX_Outbox_WorkQueue] 
    ON [dbo].[Outbox]([Status], [CreatedAt])
    INCLUDE([Id], [LockedUntil], [DueTimeUtc])
    WHERE [Status] = 0;
GO

-- Topic Index: For monitoring and debugging
CREATE INDEX [IX_Outbox_Topic] 
    ON [dbo].[Outbox]([Topic], [CreatedAt])
    WHERE [Status] IN (0, 1);
GO

-- Correlation Index: For distributed tracing
CREATE INDEX [IX_Outbox_CorrelationId] 
    ON [dbo].[Outbox]([CorrelationId])
    WHERE [CorrelationId] IS NOT NULL;
GO
