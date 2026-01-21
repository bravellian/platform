CREATE OR ALTER PROCEDURE [$SchemaName$].Lease_Acquire
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Acquired BIT OUTPUT,
    @ServerUtcNow DATETIMEOFFSET(3) OUTPUT,
    @LeaseUntilUtc DATETIMEOFFSET(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    SET @ServerUtcNow = @now;
    SET @Acquired = 0;
    SET @LeaseUntilUtc = NULL;

    BEGIN TRAN;

    MERGE [$SchemaName$].[$LeaseTable$] AS target
    USING (SELECT @Name AS Name) AS source
    ON (target.Name = source.Name)
    WHEN NOT MATCHED THEN
        INSERT (Name, Owner, LeaseUntilUtc, LastGrantedUtc)
        VALUES (source.Name, NULL, NULL, NULL);

    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET Owner = @Owner,
           LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [$SchemaName$].[$LeaseTable$] l
     WHERE l.Name = @Name
       AND (l.Owner IS NULL OR l.LeaseUntilUtc IS NULL OR l.LeaseUntilUtc <= @now);

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Acquired = 1;
        SET @LeaseUntilUtc = @newLease;
    END

    COMMIT TRAN;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].Lease_Renew
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Renewed BIT OUTPUT,
    @ServerUtcNow DATETIMEOFFSET(3) OUTPUT,
    @LeaseUntilUtc DATETIMEOFFSET(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
    DECLARE @newLease DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    SET @ServerUtcNow = @now;
    SET @Renewed = 0;
    SET @LeaseUntilUtc = NULL;

    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [$SchemaName$].[$LeaseTable$] l
     WHERE l.Name = @Name
       AND l.Owner = @Owner
       AND l.LeaseUntilUtc > @now;

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Renewed = 1;
        SET @LeaseUntilUtc = @newLease;
    END
END
GO
