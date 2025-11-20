CREATE   PROCEDURE [infra].[Semaphore_Renew]
    @Name NVARCHAR(200),
    @Token UNIQUEIDENTIFIER,
    @TtlSeconds INT,
    @Renewed BIT OUTPUT,
    @ExpiresAtUtc DATETIME2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @TtlSeconds, @now);
    DECLARE @currentExpiry DATETIME2(3);

    -- Check current expiry
    SELECT @currentExpiry = [LeaseUntilUtc]
    FROM [infra].[SemaphoreLease]
    WHERE [Name] = @Name AND [Token] = @Token;

    -- If not found or expired, return Lost
    IF @currentExpiry IS NULL OR @currentExpiry <= @now
    BEGIN
        SET @Renewed = 0;
        SET @ExpiresAtUtc = NULL;
        RETURN;
    END

    -- Monotonic extension: only extend if new expiry is later
    IF @until > @currentExpiry
    BEGIN
        SET @ExpiresAtUtc = @until;
    END
    ELSE
    BEGIN
        SET @ExpiresAtUtc = @currentExpiry;
    END

    -- Update lease
    UPDATE [infra].[SemaphoreLease]
    SET [LeaseUntilUtc] = @ExpiresAtUtc,
        [RenewedUtc] = @now
    WHERE [Name] = @Name AND [Token] = @Token;

    SET @Renewed = 1;
END