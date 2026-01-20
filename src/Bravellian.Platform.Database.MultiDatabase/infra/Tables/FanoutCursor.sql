CREATE TABLE [infra].[FanoutCursor] (
    [FanoutTopic]     NVARCHAR (100)     NOT NULL,
    [WorkKey]         NVARCHAR (100)     NOT NULL,
    [ShardKey]        NVARCHAR (256)     NOT NULL,
    [LastCompletedAt] DATETIMEOFFSET (7) NULL,
    [CreatedAt]       DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [UpdatedAt]       DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    CONSTRAINT [PK_FanoutCursor] PRIMARY KEY CLUSTERED ([FanoutTopic] ASC, [WorkKey] ASC, [ShardKey] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_FanoutCursor_TopicWork]
    ON [infra].[FanoutCursor]([FanoutTopic] ASC, [WorkKey] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_FanoutCursor_LastCompleted]
    ON [infra].[FanoutCursor]([LastCompletedAt] ASC) WHERE ([LastCompletedAt] IS NOT NULL);

