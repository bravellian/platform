CREATE   PROCEDURE [infra].[Lock_Release]
    @ResourceName SYSNAME,
    @OwnerToken UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [infra].[DistributedLock] WITH (UPDLOCK, ROWLOCK)
       SET OwnerToken = NULL,
           LeaseUntil = NULL,
           ContextJson = NULL
     WHERE ResourceName = @ResourceName
       AND OwnerToken   = @OwnerToken;
END