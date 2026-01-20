CREATE   PROCEDURE [infra].[Lock_Renew]
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
      FROM [infra].[DistributedLock] dl
     WHERE dl.ResourceName = @ResourceName
       AND dl.OwnerToken   = @OwnerToken
       AND dl.LeaseUntil   > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT @FencingToken = FencingToken
          FROM [infra].[DistributedLock]
         WHERE ResourceName = @ResourceName;
        SET @Renewed = 1;
    END
    ELSE
    BEGIN
        SET @Renewed = 0; SET @FencingToken = NULL;
    END
END