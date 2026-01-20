CREATE TABLE [infra].[Outbox] (
    [Id]            UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [Payload]       NVARCHAR (MAX)     NOT NULL,
    [Topic]         NVARCHAR (255)     NOT NULL,
    [CreatedAt]     DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [IsProcessed]   BIT                DEFAULT ((0)) NOT NULL,
    [ProcessedAt]   DATETIMEOFFSET (7) NULL,
    [ProcessedBy]   NVARCHAR (100)     NULL,
    [RetryCount]    INT                DEFAULT ((0)) NOT NULL,
    [LastError]     NVARCHAR (MAX)     NULL,
    [MessageId]     UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [CorrelationId] NVARCHAR (255)     NULL,
    [DueTimeUtc]    DATETIMEOFFSET (3) NULL,
    [Status]        TINYINT            DEFAULT ((0)) NOT NULL,
    [LockedUntil]   DATETIMEOFFSET (3) NULL,
    [OwnerToken]    UNIQUEIDENTIFIER   NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_Outbox_WorkQueue]
    ON [infra].[Outbox]([Status] ASC, [CreatedAt] ASC)
    INCLUDE([Id], [LockedUntil], [DueTimeUtc]) WHERE ([Status]=(0));

