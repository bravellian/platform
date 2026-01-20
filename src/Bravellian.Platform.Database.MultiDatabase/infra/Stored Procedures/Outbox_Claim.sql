CREATE   PROCEDURE [infra].[Outbox_Claim]
                @OwnerToken UNIQUEIDENTIFIER,
                @LeaseSeconds INT,
                @BatchSize INT = 50
              AS
              BEGIN
                SET NOCOUNT ON;
                DECLARE @now DATETIMEOFFSET(3) = SYSDATETIMEOFFSET();
                DECLARE @until DATETIMEOFFSET(3) = DATEADD(SECOND, @LeaseSeconds, @now);

                WITH cte AS (
                    SELECT TOP (@BatchSize) Id
                    FROM [infra].[Outbox] WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE Status = 0
                        AND (LockedUntil IS NULL OR LockedUntil <= @now)
                        AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)
                    ORDER BY CreatedAt
                )
                UPDATE o SET Status = 1, OwnerToken = @OwnerToken, LockedUntil = @until
                OUTPUT inserted.Id
                FROM [infra].[Outbox] o JOIN cte ON cte.Id = o.Id;
              END