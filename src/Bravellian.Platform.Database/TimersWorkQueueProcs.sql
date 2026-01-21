IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'infra')
BEGIN
    EXEC('CREATE SCHEMA [infra]');
END
GO

IF TYPE_ID(N'infra.GuidIdList') IS NULL
BEGIN
    CREATE TYPE infra.GuidIdList AS TABLE (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'infra.Timers_Claim', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE infra.Timers_Claim AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE infra.Timers_Claim
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
        FROM infra.Timers WITH (READPAST, UPDLOCK, ROWLOCK)
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
    FROM infra.Timers t
    JOIN cte ON cte.Id = t.Id;
END
GO

CREATE OR ALTER PROCEDURE infra.Timers_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t SET
        StatusCode = 2,
        OwnerToken = NULL,
        LockedUntil = NULL,
        ProcessedAt = SYSUTCDATETIME(),
        Status = 'Processed'
    FROM infra.Timers t
    JOIN @Ids i ON i.Id = t.Id
    WHERE t.OwnerToken = @OwnerToken AND t.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Timers_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY,
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
    FROM infra.Timers t
    JOIN @Ids i ON i.Id = t.Id
    WHERE t.OwnerToken = @OwnerToken AND t.StatusCode = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Timers_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE infra.Timers
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
