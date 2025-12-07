/*
 * Outbox_Ack - Acknowledge Successful Processing
 * 
 * Purpose: Mark messages as successfully processed
 * Pattern: Only acknowledges messages owned by caller
 * 
 * Parameters:
 *   @OwnerToken - Must match the token from Claim
 *   @Ids - List of message IDs to acknowledge
 * 
 * Side Effects:
 * - Updates OutboxJoin tracking if OutboxJoinMember exists
 * - Increments CompletedSteps counter atomically
 * 
 * Security:
 * - OwnerToken validation prevents one worker from
 *   acknowledging another worker's messages
 */

CREATE OR ALTER PROCEDURE [dbo].[Outbox_Ack]
    @OwnerToken UNIQUEIDENTIFIER,
    @Ids [dbo].[GuidIdList] READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Mark outbox messages as dispatched
    UPDATE o 
    SET [Status] = 2, /* Done */
        [OwnerToken] = NULL, 
        [LockedUntil] = NULL, 
        [IsProcessed] = 1, 
        [ProcessedAt] = SYSUTCDATETIME()
    FROM [dbo].[Outbox] o 
    JOIN @Ids i ON i.[Id] = o.[Id]
    WHERE o.[OwnerToken] = @OwnerToken 
      AND o.[Status] = 1; /* InProgress */
    
    -- Only proceed with join updates if any messages were actually acknowledged
    -- and OutboxJoin tables exist (i.e., join feature is enabled)
    IF @@ROWCOUNT > 0 
       AND OBJECT_ID(N'[dbo].[OutboxJoinMember]', N'U') IS NOT NULL
    BEGIN
        -- Mark join members as completed (idempotent)
        UPDATE m
        SET [CompletedAt] = SYSUTCDATETIME()
        FROM [dbo].[OutboxJoinMember] m
        INNER JOIN @Ids i ON m.[OutboxMessageId] = i.[Id]
        WHERE m.[CompletedAt] IS NULL
          AND m.[FailedAt] IS NULL;
        
        -- Increment counter for joins with newly marked members
        IF @@ROWCOUNT > 0
        BEGIN
            UPDATE j
            SET [CompletedSteps] = [CompletedSteps] + 1,
                [LastUpdatedUtc] = SYSUTCDATETIME()
            FROM [dbo].[OutboxJoin] j
            INNER JOIN [dbo].[OutboxJoinMember] m ON j.[JoinId] = m.[JoinId]
            INNER JOIN @Ids i ON m.[OutboxMessageId] = i.[Id]
            WHERE m.[CompletedAt] IS NOT NULL
              AND m.[FailedAt] IS NULL
              AND m.[CompletedAt] >= DATEADD(SECOND, -1, SYSUTCDATETIME())
              AND (j.[CompletedSteps] + j.[FailedSteps]) < j.[ExpectedSteps];
        END
    END
END
GO
