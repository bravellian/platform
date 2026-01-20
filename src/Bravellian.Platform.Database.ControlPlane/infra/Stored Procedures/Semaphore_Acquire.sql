CREATE   PROCEDURE [infra].[Semaphore_Acquire]
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

    -- Lock semaphore row for this name
    SELECT @limit = [Limit]
    FROM [infra].[Semaphore] WITH (UPDLOCK, HOLDLOCK)
    WHERE [Name] = @Name;

    -- If semaphore doesn't exist, fail
    IF @limit IS NULL
    BEGIN
        SET @Acquired = 0;
        SET @Token = NULL;
        SET @Fencing = NULL;
        SET @ExpiresAtUtc = NULL;
        COMMIT TRAN;
        RETURN;
    END

    -- Check if we have an existing lease for this client request ID
    IF @ClientRequestId IS NOT NULL
    BEGIN
        SELECT @Token = [Token], @Fencing = [Fencing], @ExpiresAtUtc = [LeaseUntilUtc]
        FROM [infra].[SemaphoreLease]
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

    -- Opportunistic reap: delete a small batch of expired leases
    DELETE TOP (10) FROM [infra].[SemaphoreLease]
    WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;

    -- Count active leases
    SELECT @activeCount = COUNT(*)
    FROM [infra].[SemaphoreLease]
    WHERE [Name] = @Name AND [LeaseUntilUtc] > @now;

    -- Check if we can acquire
    IF @activeCount >= @limit
    BEGIN
        SET @Acquired = 0;
        SET @Token = NULL;
        SET @Fencing = NULL;
        SET @ExpiresAtUtc = NULL;
        COMMIT TRAN;
        RETURN;
    END

    -- Acquire the lease
    SET @Token = NEWID();

    -- Get and increment fencing counter
    UPDATE [infra].[Semaphore]
    SET @Fencing = [NextFencingCounter],
        [NextFencingCounter] = [NextFencingCounter] + 1,
        [UpdatedUtc] = @now
    WHERE [Name] = @Name;

    -- Insert lease
    INSERT INTO [infra].[SemaphoreLease]
        ([Name], [Token], [Fencing], [OwnerId], [LeaseUntilUtc], [CreatedUtc], [ClientRequestId])
    VALUES
        (@Name, @Token, @Fencing, @OwnerId, @until, @now, @ClientRequestId);

    SET @Acquired = 1;
    SET @ExpiresAtUtc = @until;

    COMMIT TRAN;
END