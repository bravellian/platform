CREATE   PROCEDURE [infra].[Inbox_Claim]
                @OwnerToken UNIQUEIDENTIFIER,
                @LeaseSeconds INT,
                @BatchSize INT = 50
              AS
              BEGIN
                SET NOCOUNT ON;
                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                WITH cte AS (
                    SELECT TOP (@BatchSize) MessageId
                    FROM [infra].[Inbox] WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE Status IN ('Seen', 'Processing')
                        AND (LockedUntil IS NULL OR LockedUntil <= @now)
                        AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
                    ORDER BY LastSeenUtc
                )
                UPDATE i SET Status = 'Processing', OwnerToken = @OwnerToken, LockedUntil = @until, LastSeenUtc = @now
                OUTPUT inserted.MessageId
                FROM [infra].[Inbox] i JOIN cte ON cte.MessageId = i.MessageId;
              END