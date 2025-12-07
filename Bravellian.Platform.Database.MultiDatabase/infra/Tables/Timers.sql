CREATE TABLE [infra].[Timers] (
    [Id]            UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [DueTime]       DATETIMEOFFSET (7) NOT NULL,
    [Payload]       NVARCHAR (MAX)     NOT NULL,
    [Topic]         NVARCHAR (255)     NOT NULL,
    [CorrelationId] NVARCHAR (255)     NULL,
    [Status]        NVARCHAR (20)      DEFAULT ('Pending') NOT NULL,
    [ClaimedBy]     NVARCHAR (100)     NULL,
    [ClaimedAt]     DATETIMEOFFSET (7) NULL,
    [RetryCount]    INT                DEFAULT ((0)) NOT NULL,
    [CreatedAt]     DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [ProcessedAt]   DATETIMEOFFSET (7) NULL,
    [LastError]     NVARCHAR (MAX)     NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_Timers_GetNext]
    ON [infra].[Timers]([Status] ASC, [DueTime] ASC)
    INCLUDE([Id], [Topic]) WHERE ([Status]='Pending');

