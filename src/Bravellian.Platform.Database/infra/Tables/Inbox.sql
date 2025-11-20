CREATE TABLE [infra].[Inbox] (
    [MessageId]    VARCHAR (64)     NOT NULL,
    [Source]       VARCHAR (64)     NOT NULL,
    [Hash]         BINARY (32)      NULL,
    [FirstSeenUtc] DATETIME2 (3)    DEFAULT (getutcdate()) NOT NULL,
    [LastSeenUtc]  DATETIME2 (3)    DEFAULT (getutcdate()) NOT NULL,
    [ProcessedUtc] DATETIME2 (3)    NULL,
    [DueTimeUtc]   DATETIME2 (3)    NULL,
    [Attempts]     INT              DEFAULT ((0)) NOT NULL,
    [Status]       VARCHAR (16)     DEFAULT ('Seen') NOT NULL,
    [LockedUntil]  DATETIME2 (3)    NULL,
    [OwnerToken]   UNIQUEIDENTIFIER NULL,
    [Topic]        VARCHAR (128)    NULL,
    [Payload]      NVARCHAR (MAX)   NULL,
    PRIMARY KEY CLUSTERED ([MessageId] ASC),
    CONSTRAINT [CK_Inbox_Status] CHECK ([Status]='Dead' OR [Status]='Done' OR [Status]='Processing' OR [Status]='Seen')
);


GO
CREATE NONCLUSTERED INDEX [IX_Inbox_WorkQueue]
    ON [infra].[Inbox]([Status] ASC, [LastSeenUtc] ASC)
    INCLUDE([MessageId], [OwnerToken]) WHERE ([Status] IN ('Seen', 'Processing'));


GO
CREATE NONCLUSTERED INDEX [IX_Inbox_Status_ProcessedUtc]
    ON [infra].[Inbox]([Status] ASC, [ProcessedUtc] ASC) WHERE ([Status]='Done' AND [ProcessedUtc] IS NOT NULL);


GO
CREATE NONCLUSTERED INDEX [IX_Inbox_Status]
    ON [infra].[Inbox]([Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Inbox_ProcessedUtc]
    ON [infra].[Inbox]([ProcessedUtc] ASC) WHERE ([ProcessedUtc] IS NOT NULL);

