/*
 * Outbox_Claim - Atomic Work Queue Claim
 * 
 * Purpose: Atomically claim a batch of ready outbox messages for processing
 * Pattern: Uses READPAST, UPDLOCK, ROWLOCK for non-blocking atomic claims
 * 
 * Parameters:
 *   @OwnerToken - Unique identifier for the claiming process
 *   @LeaseSeconds - How long to hold the lease
 *   @BatchSize - Maximum number of messages to claim (default 50)
 * 
 * Returns: Table of claimed message IDs
 * 
 * Guarantees:
 * - Only claims Ready (Status=0) messages
 * - Respects DueTimeUtc (won't claim messages not yet due)
 * - Won't claim messages with active leases
 * - Multiple workers can claim concurrently without conflicts
 * 
 * Performance:
 * - READPAST: Skips locked rows (no blocking)
 * - UPDLOCK: Prevents deadlocks
 * - ROWLOCK: Minimizes lock scope
 */

CREATE OR ALTER PROCEDURE [dbo].[Outbox_Claim]
    @OwnerToken UNIQUEIDENTIFIER,
    @LeaseSeconds INT,
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @until DATETIME2(3) = DATEADD(SECOND, @LeaseSeconds, @now);

    -- Atomically claim ready messages
    WITH cte AS (
        SELECT TOP (@BatchSize) [Id]
        FROM [dbo].[Outbox] WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE [Status] = 0 /* Ready */
          AND ([LockedUntil] IS NULL OR [LockedUntil] <= @now)
          AND ([DueTimeUtc] IS NULL OR [DueTimeUtc] <= @now)
        ORDER BY [CreatedAt]
    )
    UPDATE o 
    SET [Status] = 1, /* InProgress */
        [OwnerToken] = @OwnerToken, 
        [LockedUntil] = @until
    OUTPUT inserted.[Id]
    FROM [dbo].[Outbox] o 
    JOIN cte ON cte.[Id] = o.[Id];
END
GO
