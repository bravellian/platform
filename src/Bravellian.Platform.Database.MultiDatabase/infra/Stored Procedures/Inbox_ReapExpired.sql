CREATE   PROCEDURE [infra].[Inbox_ReapExpired]
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE [infra].[Inbox] SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSDATETIMEOFFSET()
                WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSDATETIMEOFFSET();
                SELECT @@ROWCOUNT AS ReapedCount;
              END