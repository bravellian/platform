-- Work Queue Stored Procedures for JobRuns Table
-- Implements claim-and-process pattern with atomic operations

-- JobRuns Claim Procedure (claims due job runs)
CREATE OR ALTER PROCEDURE dbo.JobRuns_Claim
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
        FROM dbo.JobRuns WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0 /* Ready */
          AND ScheduledTime <= @now /* Only due job runs */
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY ScheduledTime
    )
    UPDATE jr
       SET StatusCode = 1 /* InProgress */, 
           OwnerToken = @OwnerToken, 
           LockedUntil = @until,
           ClaimedBy = CAST(@OwnerToken AS NVARCHAR(100)),
           ClaimedAt = @now,
           StartTime = @now
      OUTPUT inserted.Id
      FROM dbo.JobRuns jr
      JOIN cte ON cte.Id = jr.Id;
END
GO

-- JobRuns Acknowledge Procedure
CREATE OR ALTER PROCEDURE dbo.JobRuns_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE jr
       SET StatusCode = 2 /* Done */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Succeeded',
           EndTime = SYSUTCDATETIME()
      FROM dbo.JobRuns jr
      JOIN @Ids i ON i.Id = jr.Id
     WHERE jr.OwnerToken = @OwnerToken
       AND jr.StatusCode = 1; /* Only ack items currently in progress */
END
GO

-- JobRuns Abandon Procedure
CREATE OR ALTER PROCEDURE dbo.JobRuns_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE jr
       SET StatusCode = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Pending',
           ClaimedBy = NULL,
           ClaimedAt = NULL,
           StartTime = NULL
      FROM dbo.JobRuns jr
      JOIN @Ids i ON i.Id = jr.Id
     WHERE jr.OwnerToken = @OwnerToken
       AND jr.StatusCode = 1; /* Only abandon items currently in progress */
END
GO

-- JobRuns Fail Procedure
CREATE OR ALTER PROCEDURE dbo.JobRuns_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE jr
       SET StatusCode = 3 /* Failed */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Failed',
           RetryCount = RetryCount + 1,
           LastError = 'Failed by work queue client',
           EndTime = SYSUTCDATETIME()
      FROM dbo.JobRuns jr
      JOIN @Ids i ON i.Id = jr.Id
     WHERE jr.OwnerToken = @OwnerToken
       AND jr.StatusCode = 1; /* Only fail items currently in progress */
END
GO

-- JobRuns Reap Expired Procedure
CREATE OR ALTER PROCEDURE dbo.JobRuns_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    
    UPDATE dbo.JobRuns
       SET StatusCode = 0 /* Ready */, 
           OwnerToken = NULL, 
           LockedUntil = NULL,
           Status = 'Pending',
           ClaimedBy = NULL,
           ClaimedAt = NULL,
           StartTime = NULL
     WHERE StatusCode = 1 /* InProgress */
       AND LockedUntil IS NOT NULL
       AND LockedUntil <= @now;
       
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO