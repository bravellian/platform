CREATE   PROCEDURE [infra].[Inbox_Abandon]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[StringIdList] READONLY,
                @LastError NVARCHAR(MAX) = NULL,
                @DueTimeUtc DATETIMEOFFSET(3) = NULL
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE i SET
                    Status = 'Seen',
                    OwnerToken = NULL,
                    LockedUntil = NULL,
                    LastSeenUtc = SYSDATETIMEOFFSET(),
                    Attempts = Attempts + 1,
                    LastError = ISNULL(@LastError, i.LastError),
                    DueTimeUtc = ISNULL(@DueTimeUtc, i.DueTimeUtc)
                FROM [infra].[Inbox] i JOIN @Ids ids ON ids.Id = i.MessageId
                WHERE i.OwnerToken = @OwnerToken AND i.Status = 'Processing';
              END