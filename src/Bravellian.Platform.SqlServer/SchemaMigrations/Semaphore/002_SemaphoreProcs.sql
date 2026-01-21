CREATE OR ALTER PROCEDURE [$SchemaName$].Semaphore_Acquire
    @Name NVARCHAR(200),
    @OwnerId NVARCHAR(200),
    @TtlSeconds INT,
    @ClientRequestId NVARCHAR(100) = NULL,
    @Acquired BIT OUTPUT,
    @Token UNIQUEIDENTIFIER OUTPUT,
    @Fencing BIGINT OUTPUT,
    @ExpiresAtUtc DATETIMEOFFSET(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @TtlSeconds, @now);
    DECLARE @activeCount INT;
    DECLARE @limit INT;

    BEGIN TRAN;

    SELECT @limit = [Limit]
    FROM [$SchemaName$].Semaphore WITH (UPDLOCK, HOLDLOCK)
    WHERE [Name] = @Name;

    IF @limit IS NULL
    BEGIN
        SET @Acquired = 0;
        SET @Token = NULL;
        SET @Fencing = NULL;
        SET @ExpiresAtUtc = NULL;
        COMMIT TRAN;
        RETURN;
    END

    IF @ClientRequestId IS NOT NULL
    BEGIN
        SELECT @Token = [Token], @Fencing = [Fencing], @ExpiresAtUtc = [LeaseUntilUtc]
        FROM [$SchemaName$].SemaphoreLease
        WHERE [Name] = @Name
            AND [ClientRequestId] = @ClientRequestId
            AND [LeaseUntilUtc] > @now;

        IF @Token IS NOT NULL
        BEGIN
            SET @Acquired = 1;
            COMMIT TRAN;
            RETURN;
        END
    END

    DELETE TOP (10) FROM [$SchemaName$].SemaphoreLease
    WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;

    SELECT @activeCount = COUNT(*)
    FROM [$SchemaName$].SemaphoreLease
    WHERE [Name] = @Name AND [LeaseUntilUtc] > @now;

    IF @activeCount >= @limit
    BEGIN
        SET @Acquired = 0;
        SET @Token = NULL;
        SET @Fencing = NULL;
        SET @ExpiresAtUtc = NULL;
        COMMIT TRAN;
        RETURN;
    END

    SET @Token = NEWID();

    UPDATE [$SchemaName$].Semaphore
    SET @Fencing = [NextFencingCounter],
        [NextFencingCounter] = [NextFencingCounter] + 1,
        [UpdatedUtc] = @now
    WHERE [Name] = @Name;

    INSERT INTO [$SchemaName$].SemaphoreLease
        ([Name], [Token], [Fencing], [OwnerId], [LeaseUntilUtc], [CreatedUtc], [ClientRequestId])
    VALUES
        (@Name, @Token, @Fencing, @OwnerId, @until, @now, @ClientRequestId);

    SET @Acquired = 1;
    SET @ExpiresAtUtc = @until;

    COMMIT TRAN;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Semaphore_Renew
    @Name NVARCHAR(200),
    @Token UNIQUEIDENTIFIER,
    @TtlSeconds INT,
    @Renewed BIT OUTPUT,
    @ExpiresAtUtc DATETIMEOFFSET(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @TtlSeconds, @now);
    DECLARE @currentExpiry DATETIMEOFFSET(3);

    SELECT @currentExpiry = [LeaseUntilUtc]
    FROM [$SchemaName$].SemaphoreLease
    WHERE [Name] = @Name AND [Token] = @Token;

    IF @currentExpiry IS NULL OR @currentExpiry <= @now
    BEGIN
        SET @Renewed = 0;
        SET @ExpiresAtUtc = NULL;
        RETURN;
    END

    IF @until > @currentExpiry
    BEGIN
        SET @ExpiresAtUtc = @until;
    END
    ELSE
    BEGIN
        SET @ExpiresAtUtc = @currentExpiry;
    END

    UPDATE [$SchemaName$].SemaphoreLease
    SET [LeaseUntilUtc] = @ExpiresAtUtc,
        [RenewedUtc] = @now
    WHERE [Name] = @Name AND [Token] = @Token;

    SET @Renewed = 1;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Semaphore_Release
    @Name NVARCHAR(200),
    @Token UNIQUEIDENTIFIER,
    @Released BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [$SchemaName$].SemaphoreLease
    WHERE [Name] = @Name AND [Token] = @Token;

    IF @@ROWCOUNT > 0
    BEGIN
        SET @Released = 1;
    END
    ELSE
    BEGIN
        SET @Released = 0;
    END
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Semaphore_Reap
    @Name NVARCHAR(200) = NULL,
    @MaxRows INT = 1000,
    @DeletedCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();

    IF @Name IS NULL
    BEGIN
        DELETE TOP (@MaxRows) FROM [$SchemaName$].SemaphoreLease
        WHERE [LeaseUntilUtc] <= @now;
    END
    ELSE
    BEGIN
        DELETE TOP (@MaxRows) FROM [$SchemaName$].SemaphoreLease
        WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;
    END

    SET @DeletedCount = @@ROWCOUNT;
END
GO
