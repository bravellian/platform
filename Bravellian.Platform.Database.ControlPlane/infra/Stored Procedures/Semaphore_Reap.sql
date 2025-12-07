CREATE   PROCEDURE [infra].[Semaphore_Reap]
    @Name NVARCHAR(200) = NULL,
    @MaxRows INT = 1000,
    @DeletedCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();

    IF @Name IS NULL
    BEGIN
        -- Reap across all semaphores
        DELETE TOP (@MaxRows) FROM [infra].[SemaphoreLease]
        WHERE [LeaseUntilUtc] <= @now;
    END
    ELSE
    BEGIN
        -- Reap for specific semaphore
        DELETE TOP (@MaxRows) FROM [infra].[SemaphoreLease]
        WHERE [Name] = @Name AND [LeaseUntilUtc] <= @now;
    END

    SET @DeletedCount = @@ROWCOUNT;
END