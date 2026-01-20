CREATE   PROCEDURE [infra].[Inbox_Ack]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[StringIdList] READONLY
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET Status = 'Done', OwnerToken = NULL, LockedUntil = NULL, ProcessedUtc = SYSDATETIMEOFFSET(), LastSeenUtc = SYSDATETIMEOFFSET()
                FROM [infra].[Inbox] i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END