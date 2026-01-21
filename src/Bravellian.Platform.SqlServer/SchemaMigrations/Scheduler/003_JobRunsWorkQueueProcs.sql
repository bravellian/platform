IF OBJECT_ID(N'[$SchemaName$].[$JobRunsTable$_Claim]', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE [$SchemaName$].[$JobRunsTable$_Claim] AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$JobRunsTable$_Claim]
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM [$SchemaName$].[$JobRunsTable$] WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0
          AND ScheduledTime <= @now
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY ScheduledTime, Id
    )
    UPDATE jr SET
        StatusCode = 1,
        OwnerToken = @OwnerToken,
        LockedUntil = @until,
        Status = 'Running',
        ClaimedAt = @now,
        ClaimedBy = CONVERT(NVARCHAR(36), @OwnerToken)
    OUTPUT inserted.Id
    FROM [$SchemaName$].[$JobRunsTable$] jr
    JOIN cte ON cte.Id = jr.Id;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$JobRunsTable$_Ack]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    UPDATE jr SET
        StatusCode = 2,
        OwnerToken = NULL,
        LockedUntil = NULL,
        Status = 'Succeeded',
        EndTime = ISNULL(EndTime, @now)
    FROM [$SchemaName$].[$JobRunsTable$] jr
    JOIN @Ids i ON i.Id = jr.Id
    WHERE jr.OwnerToken = @OwnerToken AND jr.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$JobRunsTable$_Abandon]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @RetryDelaySeconds INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @retryUntil DATETIME2(3) = CASE WHEN @RetryDelaySeconds IS NULL THEN NULL ELSE DATEADD(SECOND, @RetryDelaySeconds, @now) END;

    UPDATE jr SET
        StatusCode = 0,
        OwnerToken = NULL,
        LockedUntil = @retryUntil,
        RetryCount = RetryCount + 1,
        LastError = ISNULL(@LastError, jr.LastError),
        Status = 'Pending'
    FROM [$SchemaName$].[$JobRunsTable$] jr
    JOIN @Ids i ON i.Id = jr.Id
    WHERE jr.OwnerToken = @OwnerToken AND jr.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$JobRunsTable$_ReapExpired]
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [$SchemaName$].[$JobRunsTable$]
    SET StatusCode = 0,
        OwnerToken = NULL,
        LockedUntil = NULL,
        Status = 'Pending'
    WHERE StatusCode = 1
      AND LockedUntil IS NOT NULL
      AND LockedUntil <= SYSUTCDATETIME();

    SELECT @@ROWCOUNT AS ReapedCount;
END
GO
