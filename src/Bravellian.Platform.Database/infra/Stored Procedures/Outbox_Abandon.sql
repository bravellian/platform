CREATE   PROCEDURE [infra].[Outbox_Abandon]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[GuidIdList] READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET Status = 0, OwnerToken = NULL, LockedUntil = NULL
                FROM [infra].[Outbox] o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END