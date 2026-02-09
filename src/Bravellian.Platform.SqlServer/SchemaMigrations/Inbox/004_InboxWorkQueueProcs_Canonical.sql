IF OBJECT_ID(N'[$SchemaName$].[$InboxTable$_Cleanup]', N'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE [$SchemaName$].[$InboxTable$_Cleanup] AS RETURN 0;');
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_Cleanup]
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET(3) = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());
    DELETE FROM [$SchemaName$].[$InboxTable$]
     WHERE Status = 'Done'
       AND COALESCE(ProcessedOn, ProcessedUtc) IS NOT NULL
       AND COALESCE(ProcessedOn, ProcessedUtc) < @cutoffTime;
    SELECT @@ROWCOUNT AS DeletedCount;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_Claim]
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now DATETIMEOFFSET(3) = SYSUTCDATETIME();
    DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);
    WITH cte AS (
        SELECT TOP (@BatchSize) MessageId
        FROM [$SchemaName$].[$InboxTable$] WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status IN ('Seen', 'Processing')
            AND (LockedUntil IS NULL OR LockedUntil <= @now)
            AND (COALESCE(DueOn, DueTimeUtc) IS NULL OR COALESCE(DueOn, DueTimeUtc) <= @now)
        ORDER BY LastSeenUtc
    )
    UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
    OUTPUT inserted.MessageId
    FROM [$SchemaName$].[$InboxTable$] i JOIN cte ON cte.MessageId = i.MessageId;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_Ack]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].StringIdList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL,
        ProcessedUtc = SYSUTCDATETIME(),
        ProcessedOn = SYSUTCDATETIME(),
        LastSeenUtc = SYSUTCDATETIME()
    FROM [$SchemaName$].[$InboxTable$] i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_Abandon]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].StringIdList READONLY,
    @LastError NVARCHAR(MAX) = NULL,
    @DueTimeUtc DATETIMEOFFSET(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET
        Status = 'Seen',
        OwnerToken = NULL,
        LockedUntil = NULL,
        LastSeenUtc = SYSUTCDATETIME(),
        Attempts = Attempts + 1,
        AttemptCount = AttemptCount + 1,
        LastError = ISNULL(@LastError, i.LastError),
        DueTimeUtc = COALESCE(@DueTimeUtc, i.DueTimeUtc, i.DueOn),
        DueOn = COALESCE(@DueTimeUtc, i.DueOn, i.DueTimeUtc)
    FROM [$SchemaName$].[$InboxTable$] i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_Fail]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [$SchemaName$].StringIdList READONLY,
    @Reason NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE i SET
        Status = 'Dead',
        OwnerToken = NULL,
        LockedUntil = NULL,
        LastSeenUtc = SYSUTCDATETIME(),
        LastError = ISNULL(@Reason, i.LastError)
    FROM [$SchemaName$].[$InboxTable$] i JOIN @Ids ids ON ids.Id = i.MessageId
    WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].[$InboxTable$_ReapExpired]
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [$SchemaName$].[$InboxTable$]
        SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
    WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
    SELECT @@ROWCOUNT AS ReapedCount;
END
GO
