IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].MetricDef', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].MetricDef (
      MetricDefId   INT IDENTITY PRIMARY KEY,
      Name          NVARCHAR(128) NOT NULL UNIQUE,
      Unit          NVARCHAR(32)  NOT NULL,
      AggKind       NVARCHAR(16)  NOT NULL,
      Description   NVARCHAR(512) NOT NULL
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].MetricSeries', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].MetricSeries (
      SeriesId      BIGINT IDENTITY PRIMARY KEY,
      MetricDefId   INT NOT NULL REFERENCES [$SchemaName$].MetricDef(MetricDefId),
      Service       NVARCHAR(64) NOT NULL,
      InstanceId    UNIQUEIDENTIFIER NOT NULL,
      TagsJson      NVARCHAR(1024) NOT NULL DEFAULT (N'{}'),
      TagHash       VARBINARY(32) NOT NULL,
      CreatedUtc    DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
      CONSTRAINT UQ_MetricSeries UNIQUE (MetricDefId, Service, InstanceId, TagHash)
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].MetricPointMinute', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].MetricPointMinute (
      SeriesId        BIGINT       NOT NULL REFERENCES [$SchemaName$].MetricSeries(SeriesId),
      BucketStartUtc  DATETIMEOFFSET(0) NOT NULL,
      BucketSecs      SMALLINT     NOT NULL,
      ValueSum        FLOAT        NULL,
      ValueCount      INT          NULL,
      ValueMin        FLOAT        NULL,
      ValueMax        FLOAT        NULL,
      ValueLast       FLOAT        NULL,
      P50             FLOAT        NULL,
      P95             FLOAT        NULL,
      P99             FLOAT        NULL,
      InsertedUtc     DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
      CONSTRAINT PK_MetricPointMinute PRIMARY KEY (SeriesId, BucketStartUtc, BucketSecs)
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_MetricPointMinute_ByTime'
      AND object_id = OBJECT_ID(N'[$SchemaName$].MetricPointMinute', N'U'))
BEGIN
    CREATE INDEX IX_MetricPointMinute_ByTime ON [$SchemaName$].MetricPointMinute (BucketStartUtc);
END
GO
