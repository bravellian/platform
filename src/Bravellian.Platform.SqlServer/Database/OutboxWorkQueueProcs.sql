IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'infra')
BEGIN
    EXEC('CREATE SCHEMA [infra]');
END
GO

IF OBJECT_ID(N'infra.Outbox_Cleanup', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE infra.Outbox_Cleanup AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Cleanup
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET(3) = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

    DELETE FROM infra.Outbox
     WHERE Status = 2
       AND ProcessedAt IS NOT NULL
       AND ProcessedAt < @cutoffTime;

    SELECT @@ROWCOUNT AS DeletedCount;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Claim
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
        FROM infra.Outbox WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
        ORDER BY CreatedAt
    )
    UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
    OUTPUT inserted.Id
    FROM infra.Outbox o JOIN cte ON cte.Id = o.Id;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Ack
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

    IF @@ROWCOUNT > 0 AND OBJECT_ID(N'infra.OutboxJoinMember', N'U') IS NOT NULL
    BEGIN
        UPDATE m
        SET CompletedAt = SYSUTCDATETIME()
        FROM infra.OutboxJoinMember m
        INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
        WHERE m.CompletedAt IS NULL AND m.FailedAt IS NULL;

        IF @@ROWCOUNT > 0
        BEGIN
            UPDATE j
            SET CompletedSteps = CompletedSteps + 1,
                LastUpdatedUtc = SYSUTCDATETIME()
            FROM infra.OutboxJoin j
            INNER JOIN infra.OutboxJoinMember m ON j.JoinId = m.JoinId
            INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
            WHERE m.CompletedAt IS NOT NULL
                AND m.FailedAt IS NULL
                AND m.CompletedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
        END
    END
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Abandon
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @DueTimeUtc DATETIMEOFFSET(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIMEOFFSET(3) = SYSUTCDATETIME();
    UPDATE o SET
        Status = 0,
        OwnerToken = NULL,
        LockedUntil = NULL,
        RetryCount = RetryCount + 1,
        LastError = ISNULL(@LastError, o.LastError),
        DueTimeUtc = COALESCE(@DueTimeUtc, o.DueTimeUtc, @now)
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_Fail
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids infra.GuidIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @ProcessedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o SET
        Status = 3,
        OwnerToken = NULL,
        LockedUntil = NULL,
        IsProcessed = 0,
        LastError = ISNULL(@LastError, o.LastError),
        ProcessedBy = ISNULL(@ProcessedBy, o.ProcessedBy)
    FROM infra.Outbox o JOIN @Ids i ON i.Id = o.Id
    WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

    IF @@ROWCOUNT > 0 AND OBJECT_ID(N'infra.OutboxJoinMember', N'U') IS NOT NULL
    BEGIN
        UPDATE m
        SET FailedAt = SYSUTCDATETIME()
        FROM infra.OutboxJoinMember m
        INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
        WHERE m.CompletedAt IS NULL AND m.FailedAt IS NULL;

        IF @@ROWCOUNT > 0
        BEGIN
            UPDATE j
            SET FailedSteps = FailedSteps + 1,
                LastUpdatedUtc = SYSUTCDATETIME()
            FROM infra.OutboxJoin j
            INNER JOIN infra.OutboxJoinMember m ON j.JoinId = m.JoinId
            INNER JOIN @Ids i ON m.OutboxMessageId = i.Id
            WHERE m.CompletedAt IS NULL
                AND m.FailedAt IS NOT NULL
                AND m.FailedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
        END
    END
END
GO

CREATE OR ALTER PROCEDURE infra.Outbox_ReapExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE infra.Outbox SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
    WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO
