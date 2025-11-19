CREATE TABLE [infra].[OutboxState] (
    [Id]                  INT           NOT NULL,
    [CurrentFencingToken] BIGINT        DEFAULT ((0)) NOT NULL,
    [LastDispatchAt]      DATETIME2 (3) NULL,
    CONSTRAINT [PK_OutboxState] PRIMARY KEY CLUSTERED ([Id] ASC)
);

