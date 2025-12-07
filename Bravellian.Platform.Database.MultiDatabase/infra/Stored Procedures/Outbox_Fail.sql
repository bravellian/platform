CREATE   PROCEDURE [infra].[Outbox_Fail]
                @OwnerToken UNIQUEIDENTIFIER,
                @Ids [infra].[GuidIdList] READONLY,
                @LastError NVARCHAR(MAX) = NULL,
                @ProcessedBy NVARCHAR(100) = NULL
              AS
              BEGIN
                SET NOCOUNT ON;

                -- Mark outbox messages as failed
                UPDATE o SET
                    Status = 3,
                    OwnerToken = NULL,
                    LockedUntil = NULL,
                    LastError = ISNULL(@LastError, o.LastError),
                    ProcessedBy = ISNULL(@ProcessedBy, o.ProcessedBy)
                FROM [infra].[Outbox] o JOIN @Ids i ON i.Id = o.Id
                WHERE o.OwnerToken = @OwnerToken AND o.Status = 1;

                -- Only proceed with join updates if any messages were actually failed
                -- and OutboxJoin tables exist (i.e., join feature is enabled)
                IF @@ROWCOUNT > 0 AND OBJECT_ID(N'[infra].[OutboxJoinMember]', N'U') IS NOT NULL
                BEGIN
                    -- First, mark the join members as failed (idempotent via WHERE clause)
                    -- This prevents race conditions by ensuring a member can only be marked once
                    UPDATE m
                    SET FailedAt = SYSDATETIMEOFFSET()
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
                            FailedSteps = FailedSteps + 1,
                            LastUpdatedUtc = SYSDATETIMEOFFSET()
                        FROM [infra].[OutboxJoin] j
                        INNER JOIN [infra].[OutboxJoinMember] m
                            ON j.JoinId = m.JoinId
                        INNER JOIN @Ids i
                            ON m.OutboxMessageId = i.Id
                        WHERE m.CompletedAt IS NULL
                            AND m.FailedAt IS NOT NULL
                            AND m.FailedAt >= DATEADD(SECOND, -1, SYSDATETIMEOFFSET())
                            AND (j.CompletedSteps + j.FailedSteps) < j.ExpectedSteps;
                    END
                END
              END