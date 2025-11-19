CREATE   PROCEDURE [infra].[Outbox_ReapExpired]
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE [infra].[Outbox] SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
                WHERE Status = 1 AND LockedUntil IS NOT NULL AND LockedUntil <= SYSUTCDATETIME();
                SELECT @@ROWCOUNT AS ReapedCount;
              END