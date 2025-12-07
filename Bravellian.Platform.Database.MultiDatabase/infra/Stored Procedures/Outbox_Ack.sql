CREATE   PROCEDURE [infra].[Outbox_Ack]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[GuidIdList] READONLY
              AS
              BEGIN
                SET NOCOUNT ON;

                -- Mark outbox messages as dispatched
                UPDATE o SET Status = 2, OwnerToken = NULL, LockedUntil = NULL, IsProcessed = 1, ProcessedAt = SYSDATETIMEOFFSET()
                FROM [infra].[Outbox] o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

                -- Only proceed with join updates if any messages were actually acknowledged
                -- and OutboxJoin tables exist (i.e., join feature is enabled)
                IF @@ROWCOUNT > 0 AND OBJECT_ID(N'[infra].[OutboxJoinMember]', N'U') IS NOT NULL
                BEGIN
                    -- First, mark the join members as completed (idempotent via WHERE clause)
                    -- This prevents race conditions by ensuring a member can only be marked once
                    UPDATE m
                    SET CompletedAt = SYSDATETIMEOFFSET()
                    FROM [infra].[OutboxJoinMember] m
                    INNER JOIN @Ids i
                        ON m.OutboxMessageId = i.Id
                    WHERE m.CompletedAt IS NULL
                        AND m.FailedAt IS NULL;

                    -- Then, increment counter ONLY for joins with members that were just marked
                    -- Using @@ROWCOUNT from previous UPDATE ensures we only count newly marked members
                    IF @@ROWCOUNT > 0
                    BEGIN
                        UPDATE j
                        SET
                            CompletedSteps = CompletedSteps + 1,
                            LastUpdatedUtc = SYSDATETIMEOFFSET()
                        FROM [infra].[OutboxJoin] j
                        INNER JOIN [infra].[OutboxJoinMember] m
                            ON j.JoinId = m.JoinId
                        INNER JOIN @Ids i
                            ON m.OutboxMessageId = i.Id
                        WHERE m.CompletedAt IS NOT NULL
                            AND m.FailedAt IS NULL
                            AND m.CompletedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                            AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
                    END
                END
              END