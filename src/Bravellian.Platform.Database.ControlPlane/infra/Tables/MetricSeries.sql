CREATE TABLE [infra].[MetricSeries] (
    [SeriesId]    BIGINT             IDENTITY (1, 1) NOT NULL,
    [MetricDefId] INT                NOT NULL,
    [DatabaseId]  UNIQUEIDENTIFIER   NULL,
    [Service]     NVARCHAR (64)      NOT NULL,
    [TagsJson]    NVARCHAR (1024)    DEFAULT (N'{"}') NOT NULL,
    [TagHash]     VARBINARY (32)     NOT NULL,
    [CreatedUtc]  DATETIMEOFFSET (3) DEFAULT (sysdatetimeoffset()) NOT NULL,
    PRIMARY KEY CLUSTERED ([SeriesId] ASC),
    FOREIGN KEY ([MetricDefId]) REFERENCES [infra].[MetricDef] ([MetricDefId]),
    CONSTRAINT [UQ_MetricSeries] UNIQUE NONCLUSTERED ([MetricDefId] ASC, [DatabaseId] ASC, [Service] ASC, [TagHash] ASC)
);

