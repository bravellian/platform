CREATE   PROCEDURE [infra].[Lease_Renew]
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Renewed BIT OUTPUT,
    @ServerUtcNow DATETIME2(3) OUTPUT,
    @LeaseUntilUtc DATETIME2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    SET @ServerUtcNow = @now;
    SET @Renewed = 0;
    SET @LeaseUntilUtc = NULL;

    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [infra].[Lease] l
     WHERE l.Name = @Name
       AND l.Owner = @Owner
       AND l.LeaseUntilUtc > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Renewed = 1;
        SET @LeaseUntilUtc = @newLease;
    END
END