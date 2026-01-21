CREATE OR ALTER PROCEDURE [$SchemaName$].Lock_Acquire
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

    IF NOT EXISTS (SELECT 1 FROM [$SchemaName$].[$LockTable$] WITH (UPDLOCK, HOLDLOCK)
                   WHERE ResourceName = @ResourceName)
    BEGIN
        INSERT [$SchemaName$].[$LockTable$] (ResourceName, OwnerToken, LeaseUntil, ContextJson)
        VALUES (@ResourceName, NULL, NULL, NULL);
    END

    UPDATE dl WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken =
             CASE WHEN dl.OwnerToken = @OwnerToken THEN dl.OwnerToken ELSE @OwnerToken END,
           LeaseUntil = @newLease,
           ContextJson = @ContextJson,
           FencingToken =
             CASE WHEN dl.OwnerToken = @OwnerToken
                  THEN dl.FencingToken + 1
                  ELSE dl.FencingToken + 1
             END
      FROM [$SchemaName$].[$LockTable$] dl
     WHERE dl.ResourceName = @ResourceName
       AND (dl.OwnerToken IS NULL OR dl.LeaseUntil IS NULL OR dl.LeaseUntil <= @now OR dl.OwnerToken = @OwnerToken);

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [$SchemaName$].[$LockTable$]
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
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Lock_Renew
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @Renewed BIT OUTPUT,
    @FencingToken BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    UPDATE dl WITH (UPDLOCK, ROWLOCK)
       SET LeaseUntil = @newLease,
           FencingToken = dl.FencingToken + 1
      FROM [$SchemaName$].[$LockTable$] dl
     WHERE dl.ResourceName = @ResourceName
       AND dl.OwnerToken   = @OwnerToken
       AND dl.LeaseUntil   > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [$SchemaName$].[$LockTable$]
         WHERE ResourceName = @ResourceName;
        SET @Renewed = 1;
    END
    ELSE
    BEGIN
        SET @Renewed = 0; SET @FencingToken = NULL;
    END
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Lock_Release
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$SchemaName$].[$LockTable$] WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken = NULL,
           LeaseUntil = NULL,
           ContextJson = NULL
     WHERE ResourceName = @ResourceName
       AND OwnerToken   = @OwnerToken;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Lock_CleanupExpired
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [$SchemaName$].[$LockTable$]
       SET OwnerToken = NULL, LeaseUntil = NULL, ContextJson = NULL
     WHERE LeaseUntil IS NOT NULL AND LeaseUntil <= SYSDATETIMEOFFSET();
END
GO
