CREATE TABLE [infra].[FanoutPolicy] (
    [FanoutTopic]         NVARCHAR (100)     NOT NULL,
    [WorkKey]             NVARCHAR (100)     NOT NULL,
    [DefaultEverySeconds] INT                NOT NULL,
    [JitterSeconds]       INT                DEFAULT ((60)) NOT NULL,
    [CreatedAt]           DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [UpdatedAt]           DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    CONSTRAINT [PK_FanoutPolicy] PRIMARY KEY CLUSTERED ([FanoutTopic] ASC, [WorkKey] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_FanoutPolicy_FanoutTopic]
    ON [infra].[FanoutPolicy]([FanoutTopic] ASC);

