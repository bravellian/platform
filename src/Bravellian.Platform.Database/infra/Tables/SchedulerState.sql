CREATE TABLE [infra].[SchedulerState] (
    [Id]                  INT           NOT NULL,
    [CurrentFencingToken] BIGINT        DEFAULT ((0)) NOT NULL,
    [LastRunAt]           DATETIME2 (3) NULL,
    CONSTRAINT [PK_SchedulerState] PRIMARY KEY CLUSTERED ([Id] ASC)
);

