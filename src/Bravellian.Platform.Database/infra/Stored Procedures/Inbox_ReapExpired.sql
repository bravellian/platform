CREATE   PROCEDURE [infra].[Inbox_ReapExpired]
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE [infra].[Inbox] SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
                WHERE Status = 'Processing' AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
                SELECT @@ROWCOUNT AS ReapedCount;
              END