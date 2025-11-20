CREATE   PROCEDURE [infra].[Lease_Acquire]
    @Name SYSNAME,
    @Owner SYSNAME,
    @LeaseSeconds INT,
    @Acquired BIT OUTPUT,
    @ServerUtcNow DATETIME2(3) OUTPUT,
    @LeaseUntilUtc DATETIME2(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @newLease DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    SET @ServerUtcNow = @now;
    SET @Acquired = 0;
    SET @LeaseUntilUtc = NULL;

    BEGIN TRAN;

    -- Ensure row exists atomically
    MERGE [infra].[Lease] AS target
    USING (SELECT @Name AS Name) AS source
    ON (target.Name = source.Name)
    WHEN NOT MATCHED THEN
        INSERT (Name, Owner, LeaseUntilUtc, LastGrantedUtc)
        VALUES (source.Name, NULL, NULL, NULL);

    -- Try to acquire lease if free or expired
    UPDATE l WITH (UPDLOCK, ROWLOCK)
       SET Owner = @Owner,
           LeaseUntilUtc = @newLease,
           LastGrantedUtc = @now
      FROM [infra].[Lease] l
     WHERE l.Name = @Name
       AND (l.Owner IS NULL OR l.LeaseUntilUtc IS NULL OR l.LeaseUntilUtc <= @now);

    IF @@ROWCOUNT = 1
    BEGIN
        SET @Acquired = 1;
        SET @LeaseUntilUtc = @newLease;
    END

    COMMIT TRAN;
END