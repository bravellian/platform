CREATE   PROCEDURE [infra].[Semaphore_Release]
    @Name NVARCHAR(200),
    @Token UNIQUEIDENTIFIER,
    @Released BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [infra].[SemaphoreLease]
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