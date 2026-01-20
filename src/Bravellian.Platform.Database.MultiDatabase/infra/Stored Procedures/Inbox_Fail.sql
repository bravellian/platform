CREATE   PROCEDURE [infra].[Inbox_Fail]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[StringIdList] READONLY,
                @Reason NVARCHAR(MAX) = NULL
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET
                    Status = 'Dead',
                    OwnerToken = NULL,
                    LockedUntil = NULL,
                    LastSeenUtc = SYSDATETIMEOFFSET(),
                    LastError = ISNULL(@Reason, i.LastError)
                FROM [infra].[Inbox] i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END