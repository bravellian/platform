CREATE TABLE [infra].[JobRuns] (
    [Id]            UNIQUEIDENTIFIER   DEFAULT (newid()) NOT NULL,
    [JobId]         UNIQUEIDENTIFIER   NOT NULL,
    [ScheduledTime] DATETIMEOFFSET (7) NOT NULL,
    [Status]        NVARCHAR (20)      DEFAULT ('Pending') NOT NULL,
    [ClaimedBy]     NVARCHAR (100)     NULL,
    [ClaimedAt]     DATETIMEOFFSET (7) NULL,
    [RetryCount]    INT                DEFAULT ((0)) NOT NULL,
    [StartTime]     DATETIMEOFFSET (7) NULL,
    [EndTime]       DATETIMEOFFSET (7) NULL,
    [Output]        NVARCHAR (MAX)     NULL,
    [LastError]     NVARCHAR (MAX)     NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    FOREIGN KEY ([JobId]) REFERENCES [infra].[Jobs] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_JobRuns_GetNext]
    ON [infra].[JobRuns]([Status] ASC, [ScheduledTime] ASC) WHERE ([Status]='Pending');

