CREATE   PROCEDURE [infra].[Lock_CleanupExpired]
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [infra].[DistributedLock]
       SET OwnerToken = NULL, LeaseUntil = NULL, ContextJson = NULL
     WHERE LeaseUntil IS NOT NULL AND LeaseUntil <= SYSUTCDATETIME();
END