CREATE TABLE [infra].[Jobs] (
    [Id]            UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [JobName]       NVARCHAR (100)     NOT NULL,
    [CronSchedule]  NVARCHAR (100)     NOT NULL,
    [Topic]         NVARCHAR (255)     NOT NULL,
    [Payload]       NVARCHAR (MAX)     NULL,
    [IsEnabled]     BIT                DEFAULT ((1)) NOT NULL,
    [NextDueTime]   DATETIMEOFFSET (7) NULL,
    [LastRunTime]   DATETIMEOFFSET (7) NULL,
    [LastRunStatus] NVARCHAR (20)      NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Jobs_JobName]
    ON [infra].[Jobs]([JobName] ASC);

