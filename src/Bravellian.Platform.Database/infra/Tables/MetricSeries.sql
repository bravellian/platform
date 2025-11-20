CREATE TABLE [infra].[MetricSeries] (
    [SeriesId]    BIGINT           IDENTITY (1, 1) NOT NULL,
    [MetricDefId] INT              NOT NULL,
    [Service]     NVARCHAR (64)    NOT NULL,
    [InstanceId]  UNIQUEIDENTIFIER NOT NULL,
    [TagsJson]    NVARCHAR (1024)  DEFAULT (N'{}') NOT NULL,
    [TagHash]     VARBINARY (32)   NOT NULL,
    [CreatedUtc]  DATETIME2 (3)    DEFAULT (sysutcdatetime()) NOT NULL,
    PRIMARY KEY CLUSTERED ([SeriesId] ASC),
    FOREIGN KEY ([MetricDefId]) REFERENCES [infra].[MetricDef] ([MetricDefId]),
    CONSTRAINT [UQ_MetricSeries] UNIQUE NONCLUSTERED ([MetricDefId] ASC, [Service] ASC, [InstanceId] ASC, [TagHash] ASC)
);

