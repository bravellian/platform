CREATE TABLE [infra].[MetricPointHourly] (
    [SeriesId]       BIGINT             NOT NULL,
    [BucketStartUtc] DATETIMEOFFSET (0) NOT NULL,
    [BucketSecs]     INT                NOT NULL,
    [ValueSum]       FLOAT (53)         NULL,
    [ValueCount]     INT                NULL,
    [ValueMin]       FLOAT (53)         NULL,
    [ValueMax]       FLOAT (53)         NULL,
    [ValueLast]      FLOAT (53)         NULL,
    [P50]            FLOAT (53)         NULL,
    [P95]            FLOAT (53)         NULL,
    [P99]            FLOAT (53)         NULL,
    [InsertedUtc]    DATETIMEOFFSET (3) DEFAULT (sysdatetimeoffset()) NOT NULL,
    CONSTRAINT [PK_MetricPointHourly] PRIMARY KEY NONCLUSTERED ([SeriesId] ASC, [BucketStartUtc] ASC, [BucketSecs] ASC),
    FOREIGN KEY ([SeriesId]) REFERENCES [infra].[MetricSeries] ([SeriesId])
);


GO
CREATE CLUSTERED COLUMNSTORE INDEX [CCI_MetricPointHourly]
    ON [infra].[MetricPointHourly];

