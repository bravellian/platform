CREATE   PROCEDURE [infra].[Outbox_Ack]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[GuidIdList] READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSUTCDATETIME()
                FROM [infra].[Outbox] o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END