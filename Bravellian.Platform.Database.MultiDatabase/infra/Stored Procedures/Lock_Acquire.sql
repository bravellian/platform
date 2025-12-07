CREATE   PROCEDURE [infra].[Lock_Acquire]
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @ContextJson NVARCHAR(MAX) = NULL,
    @UseGate BIT = 0,
    @GateTimeoutMs INT = 200,
    @Acquired BIT OUTPUT,
    @FencingToken BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);
    DECLARE @rc INT;
    DECLARE @LockResourceName NVARCHAR(255) = CONCAT('lease:', @ResourceName);

    -- Optional micro critical section to serialize row upsert under high contention
    IF (@UseGate = 1)
    BEGIN
        EXEC @rc = sp_getapplock
            @Resource    = @LockResourceName,
            @LockMode    = 'Exclusive',
            @LockOwner   = 'Session',
            @LockTimeout = @GateTimeoutMs,
            @DbPrincipal = 'public';
        IF (@rc < 0)
        BEGIN
            SET @Acquired = 0; SET @FencingToken = NULL;
            RETURN;
        END
    END

    BEGIN TRAN;

    -- Ensure row exists, holding a key-range lock to avoid races on insert
    IF NOT EXISTS (SELECT 1 FROM [infra].[DistributedLock] WITH (UPDLOCK, HOLDLOCK)
                   WHERE ResourceName = @ResourceName)
    BEGIN
        INSERT [infra].[DistributedLock] (ResourceName, OwnerToken, LeaseUntil, ContextJson)
        VALUES (@ResourceName, NULL, NULL, NULL);
    END

    -- Take or re-take the lease (re-entrant allowed)
    UPDATE dl WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken =
             CASE WHEN dl.OwnerToken = @OwnerToken THEN dl.OwnerToken ELSE @OwnerToken END,
           LeaseUntil = @newLease,
           ContextJson = @ContextJson,
           FencingToken =
             CASE WHEN dl.OwnerToken = @OwnerToken
                  THEN dl.FencingToken + 1         -- re-entrant renew-on-acquire bumps too
                  ELSE dl.FencingToken + 1         -- new owner bumps
             END
      FROM [infra].[DistributedLock] dl
     WHERE dl.ResourceName = @ResourceName
       AND (dl.OwnerToken IS NULL OR dl.LeaseUntil IS NULL OR dl.LeaseUntil <= @now OR dl.OwnerToken = @OwnerToken);

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [infra].[DistributedLock]
         WHERE ResourceName = @ResourceName;
        SET @Acquired = 1;
    END
    ELSE
    BEGIN
        SET @Acquired = 0; SET @FencingToken = NULL;
    END

    COMMIT TRAN;

    IF (@UseGate = 1)
        EXEC sp_releaseapplock
             @Resource  = @LockResourceName,
             @LockOwner = 'Session';
END