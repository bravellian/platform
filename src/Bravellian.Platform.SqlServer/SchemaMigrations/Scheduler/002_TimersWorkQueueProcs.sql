IF OBJECT_ID(N'[$SchemaName$].[$TimersTable$_Claim]', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE [$SchemaName$].[$TimersTable$_Claim] AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$TimersTable$_Claim]
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
        FROM [$SchemaName$].[$TimersTable$] WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE StatusCode = 0
          AND DueTime <= @now
          AND (LockedUntil IS NULL OR LockedUntil <= @now)
        ORDER BY DueTime, CreatedAt
    )
    UPDATE t SET
        StatusCode = 1,
        OwnerToken = @OwnerToken,
        LockedUntil = @until
    OUTPUT inserted.Id
    FROM [$SchemaName$].[$TimersTable$] t
    JOIN cte ON cte.Id = t.Id;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$TimersTable$_Ack]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t SET
        StatusCode = 2,
        OwnerToken = NULL,
        LockedUntil = NULL,
        ProcessedAt = SYSUTCDATETIME(),
        Status = 'Processed'
    FROM [$SchemaName$].[$TimersTable$] t
    JOIN @Ids i ON i.Id = t.Id
    WHERE t.OwnerToken = @OwnerToken AND t.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$TimersTable$_Abandon]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @RetryDelaySeconds INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @retryUntil DATETIME2(3) = CASE WHEN @RetryDelaySeconds IS NULL THEN NULL ELSE DATEADD(SECOND, @RetryDelaySeconds, @now) END;

    UPDATE t SET
        StatusCode = 0,
        OwnerToken = NULL,
        LockedUntil = @retryUntil,
        RetryCount = RetryCount + 1,
        LastError = ISNULL(@LastError, t.LastError),
        Status = 'Pending'
    FROM [$SchemaName$].[$TimersTable$] t
    JOIN @Ids i ON i.Id = t.Id
    WHERE t.OwnerToken = @OwnerToken AND t.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$TimersTable$_ReapExpired]
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [$SchemaName$].[$TimersTable$]
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
