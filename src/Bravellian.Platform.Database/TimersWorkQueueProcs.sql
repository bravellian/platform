-- Work Queue Stored Procedures for Timers Table
-- Implements claim-and-process pattern with atomic operations

-- Timers Claim Procedure (claims due timers)
CREATE OR ALTER PROCEDURE dbo.Timers_Claim
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
        FROM dbo.Timers WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0 /* Ready */
          AND DueTime <= @now /* Only due timers */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY DueTime
    )
    UPDATE t
       SET StatusCode = 1 /* InProgress */, 
           OwnerToken = @OwnerToken, 
           LockedUntil = @until,
           ClaimedBy = CAST(@OwnerToken AS NVARCHAR(100)),
           ClaimedAt = @now
      OUTPUT inserted.Id
      FROM dbo.Timers t
      JOIN cte ON cte.Id = t.Id;
END
GO

-- Timers Acknowledge Procedure
CREATE OR ALTER PROCEDURE dbo.Timers_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
       SET StatusCode = 2 /* Done */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Processed',
           ProcessedAt = SYSUTCDATETIME()
      FROM dbo.Timers t
      JOIN @Ids i ON i.Id = t.Id
     WHERE t.OwnerToken = @OwnerToken
       AND t.StatusCode = 1; /* Only ack items currently in progress */
END
GO

-- Timers Abandon Procedure
CREATE OR ALTER PROCEDURE dbo.Timers_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
       SET StatusCode = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Pending',
           ClaimedBy = NULL,
           ClaimedAt = NULL
      FROM dbo.Timers t
      JOIN @Ids i ON i.Id = t.Id
     WHERE t.OwnerToken = @OwnerToken
       AND t.StatusCode = 1; /* Only abandon items currently in progress */
END
GO

-- Timers Fail Procedure
CREATE OR ALTER PROCEDURE dbo.Timers_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
       SET StatusCode = 3 /* Failed */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Failed',
           RetryCount = RetryCount + 1,
           LastError = 'Failed by work queue client'
      FROM dbo.Timers t
      JOIN @Ids i ON i.Id = t.Id
     WHERE t.OwnerToken = @OwnerToken
       AND t.StatusCode = 1; /* Only fail items currently in progress */
END
GO

-- Timers Reap Expired Procedure
CREATE OR ALTER PROCEDURE dbo.Timers_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    
    UPDATE dbo.Timers
       SET StatusCode = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Pending',
           ClaimedBy = NULL,
           ClaimedAt = NULL
     WHERE StatusCode = 1 /* InProgress */
       AND LockedUntil IS NOT NULL
       AND LockedUntil <= @now;
       
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO