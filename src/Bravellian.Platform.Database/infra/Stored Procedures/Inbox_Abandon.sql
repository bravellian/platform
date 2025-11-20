CREATE   PROCEDURE [infra].[Inbox_Abandon]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[StringIdList] READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET Status = 'Seen', OwnerToken = NULL, LockedUntil = NULL, LastSeenUtc = SYSUTCDATETIME()
                FROM [infra].[Inbox] i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END