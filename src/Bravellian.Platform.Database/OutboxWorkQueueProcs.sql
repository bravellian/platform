-- Work Queue Stored Procedures for Outbox Table
-- Implements claim-and-process pattern with atomic operations

-- Outbox Claim Procedure
CREATE OR ALTER PROCEDURE dbo.Outbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    ;WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM dbo.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0 /* Ready */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o
       SET Status = 1 /* InProgress */, 
           OwnerToken = @OwnerToken, 
           LockedUntil = @until
      OUTPUT inserted.Id
      FROM dbo.Outbox o
      JOIN cte ON cte.Id = o.Id;
END
GO

-- Outbox Acknowledge Procedure
CREATE OR ALTER PROCEDURE dbo.Outbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o
       SET Status = 2 /* Done */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           IsProcessed = 1,
           ProcessedAt = SYSUTCDATETIME()
      FROM dbo.Outbox o
      JOIN @Ids i ON i.Id = o.Id
     WHERE o.OwnerToken = @OwnerToken
       AND o.Status = 1; /* Only ack items currently in progress */
END
GO

-- Outbox Abandon Procedure
CREATE OR ALTER PROCEDURE dbo.Outbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o
       SET Status = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL
      FROM dbo.Outbox o
      JOIN @Ids i ON i.Id = o.Id
     WHERE o.OwnerToken = @OwnerToken
       AND o.Status = 1; /* Only abandon items currently in progress */
END
GO

-- Outbox Fail Procedure
CREATE OR ALTER PROCEDURE dbo.Outbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o
       SET Status = 3 /* Failed */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           RetryCount = RetryCount + 1,
           LastError = 'Failed by work queue client',
           NextAttemptAt = DATEADD(SECOND, POWER(2, RetryCount + 1), SYSUTCDATETIME()) /* Exponential backoff */
      FROM dbo.Outbox o
      JOIN @Ids i ON i.Id = o.Id
     WHERE o.OwnerToken = @OwnerToken
       AND o.Status = 1; /* Only fail items currently in progress */
END
GO

-- Outbox Reap Expired Procedure
CREATE OR ALTER PROCEDURE dbo.Outbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    
    UPDATE dbo.Outbox
       SET Status = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL
     WHERE Status = 1 /* InProgress */
       AND LockedUntil IS NOT NULL
       AND LockedUntil <= @now;
       
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO