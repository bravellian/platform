-- Work Queue Stored Procedures for Inbox Table
-- Implements claim-and-process pattern with atomic operations

-- Inbox Claim Procedure
CREATE OR ALTER PROCEDURE dbo.Inbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    ;WITH cte AS (
        SELECT TOP (@BatchSize) MessageId
        FROM dbo.Inbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status IN ('Seen', 'Processing')
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY LastSeenUtc
    )
    UPDATE i
       SET Status = 'Processing', 
           OwnerToken = @OwnerToken, 
           LockedUntil = @until,
           LastSeenUtc = @now
      OUTPUT inserted.MessageId
      FROM dbo.Inbox i
      JOIN cte ON cte.MessageId = i.MessageId;
END
GO

-- Inbox Acknowledge Procedure
CREATE OR ALTER PROCEDURE dbo.Inbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i
       SET Status = 'Done', 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           ProcessedUtc = SYSUTCDATETIME(),
           LastSeenUtc = SYSUTCDATETIME()
      FROM dbo.Inbox i
      JOIN @Ids ids ON ids.Id = i.MessageId
     WHERE i.OwnerToken = @OwnerToken
       AND i.Status = 'Processing'; /* Only ack items currently in progress */
END
GO

-- Inbox Abandon Procedure
CREATE OR ALTER PROCEDURE dbo.Inbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i
       SET Status = 'Seen', 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           LastSeenUtc = SYSUTCDATETIME()
      FROM dbo.Inbox i
      JOIN @Ids ids ON ids.Id = i.MessageId
     WHERE i.OwnerToken = @OwnerToken
       AND i.Status = 'Processing'; /* Only abandon items currently in progress */
END
GO

-- Inbox Fail Procedure
CREATE OR ALTER PROCEDURE dbo.Inbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.StringIdList READONLY,
    @Reason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i
       SET Status = 'Dead', 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           LastSeenUtc = SYSUTCDATETIME()
      FROM dbo.Inbox i
      JOIN @Ids ids ON ids.Id = i.MessageId
     WHERE i.OwnerToken = @OwnerToken
       AND i.Status = 'Processing'; /* Only fail items currently in progress */
END
GO

-- Inbox Reap Expired Procedure
CREATE OR ALTER PROCEDURE dbo.Inbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Inbox 
       SET Status = 'Seen', 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           LastSeenUtc = SYSUTCDATETIME()
     WHERE Status = 'Processing' 
       AND LockedUntil IS NOT NULL 
       AND LockedUntil <= SYSUTCDATETIME();
    
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO