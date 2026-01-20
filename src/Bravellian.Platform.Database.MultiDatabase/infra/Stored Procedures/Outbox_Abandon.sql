CREATE   PROCEDURE [infra].[Outbox_Abandon]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[GuidIdList] READONLY,
                @LastError NVARCHAR(MAX) = NULL,
                @DueTimeUtc DATETIMEOFFSET(3) = NULL
              AS
              BEGIN
                SET NOCOUNT ON;
                UPDATE o SET
                    Status = 0,
                    OwnerToken = NULL,
                    LockedUntil = NULL,
                    RetryCount = RetryCount + 1,
                    LastError = ISNULL(@LastError, o.LastError),
                    DueTimeUtc = ISNULL(@DueTimeUtc, o.DueTimeUtc)
                FROM [infra].[Outbox] o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;
              END