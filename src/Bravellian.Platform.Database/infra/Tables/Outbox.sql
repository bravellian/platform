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
    [NextAttemptAt] DATETIMEOFFSET (7) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [MessageId]     UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [CorrelationId] NVARCHAR (255)     NULL,
    [DueTimeUtc]    DATETIME2 (3)      NULL,
    [Status]        TINYINT            CONSTRAINT [DF_Outbox_Status] DEFAULT ((0)) NOT NULL,
    [LockedUntil]   DATETIME2 (3)      NULL,
    [OwnerToken]    UNIQUEIDENTIFIER   NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_Outbox_GetNext]
    ON [infra].[Outbox]([IsProcessed] ASC, [NextAttemptAt] ASC)
    INCLUDE([Id], [Payload], [Topic], [RetryCount]) WHERE ([IsProcessed]=(0));

