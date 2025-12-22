IF OBJECT_ID(N'dbo.Outbox_Cleanup', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE dbo.Outbox_Cleanup AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET(3) = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

    DELETE FROM dbo.Outbox
     WHERE Status = 2
       AND ProcessedAt IS NOT NULL
       AND ProcessedAt < @cutoffTime;

    SELECT @@ROWCOUNT AS DeletedCount;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Claim
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIMEOFFSET(3) = SYSUTCDATETIME();
    DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    WITH cte AS (
        SELECT TOP (@BatchSize) Id
        FROM dbo.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
    OUTPUT inserted.Id
    FROM dbo.Outbox o JOIN cte ON cte.Id = o.Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

    IF @@ROWCOUNT > 0 AND OBJECT_ID(N'dbo.OutboxJoinMember', N'U') IS NOT NULL
    BEGIN
        UPDATE m
        SET CompletedAt = SYSUTCDATETIME()
        FROM dbo.OutboxJoinMember m
        INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
        WHERE m.CompletedAt IS NULL AND m.FailedAt IS NULL;

        IF @@ROWCOUNT > 0
        BEGIN
            UPDATE j
            SET CompletedSteps = CompletedSteps + 1,
                LastUpdatedUtc = SYSUTCDATETIME()
            FROM dbo.OutboxJoin j
            INNER JOIN dbo.OutboxJoinMember m ON j.JoinId = m.JoinId
            INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
            WHERE m.CompletedAt IS NOT NULL
                AND m.FailedAt IS NULL
                AND m.CompletedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
        END
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @DueTimeUtc DATETIMEOFFSET(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET
        Status = 0,
        OwnerToken = NULL,
        LockedUntil = NULL,
        RetryCount = RetryCount + 1,
        LastError = ISNULL(@LastError, o.LastError),
        DueTimeUtc = ISNULL(@DueTimeUtc, o.DueTimeUtc)
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids dbo.GuidIdList READONLY,
    @Reason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET
        Status = 3,
        OwnerToken = NULL,
        LockedUntil = NULL,
        IsProcessed = 0,
        LastError = ISNULL(@Reason, o.LastError)
    FROM dbo.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.Outbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Outbox SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
    WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO
