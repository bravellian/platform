CREATE TABLE [infra].[MetricPointMinute] (
    [SeriesId]       BIGINT        NOT NULL,
    [BucketStartUtc] DATETIME2 (0) NOT NULL,
    [BucketSecs]     SMALLINT      NOT NULL,
    [ValueSum]       FLOAT (53)    NULL,
    [ValueCount]     INT           NULL,
    [ValueMin]       FLOAT (53)    NULL,
    [ValueMax]       FLOAT (53)    NULL,
    [ValueLast]      FLOAT (53)    NULL,
    [P50]            FLOAT (53)    NULL,
    [P95]            FLOAT (53)    NULL,
    [P99]            FLOAT (53)    NULL,
    [InsertedUtc]    DATETIME2 (3) DEFAULT (sysutcdatetime()) NOT NULL,
    CONSTRAINT [PK_MetricPointMinute] PRIMARY KEY CLUSTERED ([SeriesId] ASC, [BucketStartUtc] ASC, [BucketSecs] ASC),
    FOREIGN KEY ([SeriesId]) REFERENCES [infra].[MetricSeries] ([SeriesId])
);


GO
CREATE NONCLUSTERED INDEX [IX_MetricPointMinute_ByTime]
    ON [infra].[MetricPointMinute]([BucketStartUtc] ASC)
    INCLUDE([SeriesId], [ValueSum], [ValueCount], [P95]);

