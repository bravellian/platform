IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
BEGIN
    BEGIN TRY
        DECLARE @schemaSql NVARCHAR(4000) = N'CREATE SCHEMA ' + QUOTENAME('$SchemaName$');
        EXEC sp_executesql @schemaSql;
    END TRY
    BEGIN CATCH
        IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
        BEGIN
            THROW;
        END
    END CATCH
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

IF OBJECT_ID(N'[$SchemaName$].MetricDef', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].MetricDef', 'MetricDefId') IS NULL
        ALTER TABLE [$SchemaName$].MetricDef ADD MetricDefId INT IDENTITY(1,1) NOT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricDef', 'Name') IS NULL
        ALTER TABLE [$SchemaName$].MetricDef ADD Name NVARCHAR(128) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].MetricDef', 'Unit') IS NULL
        ALTER TABLE [$SchemaName$].MetricDef ADD Unit NVARCHAR(32) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].MetricDef', 'AggKind') IS NULL
        ALTER TABLE [$SchemaName$].MetricDef ADD AggKind NVARCHAR(16) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].MetricDef', 'Description') IS NULL
        ALTER TABLE [$SchemaName$].MetricDef ADD Description NVARCHAR(512) NOT NULL DEFAULT N'';
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

IF OBJECT_ID(N'[$SchemaName$].MetricSeries', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'SeriesId') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD SeriesId BIGINT IDENTITY(1,1) NOT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'MetricDefId') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD MetricDefId INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'Service') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD Service NVARCHAR(64) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'InstanceId') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD InstanceId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'TagsJson') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD TagsJson NVARCHAR(1024) NOT NULL DEFAULT (N'{}');
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'TagHash') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD TagHash VARBINARY(32) NOT NULL DEFAULT 0x;
    IF COL_LENGTH('[$SchemaName$].MetricSeries', 'CreatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].MetricSeries ADD CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
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

IF OBJECT_ID(N'[$SchemaName$].MetricPointMinute', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'SeriesId') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD SeriesId BIGINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'BucketStartUtc') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD BucketStartUtc DATETIMEOFFSET(0) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'BucketSecs') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD BucketSecs SMALLINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'ValueSum') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD ValueSum FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'ValueCount') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD ValueCount INT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'ValueMin') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD ValueMin FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'ValueMax') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD ValueMax FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'ValueLast') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD ValueLast FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'P50') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD P50 FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'P95') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD P95 FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'P99') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD P99 FLOAT NULL;
    IF COL_LENGTH('[$SchemaName$].MetricPointMinute', 'InsertedUtc') IS NULL
        ALTER TABLE [$SchemaName$].MetricPointMinute ADD InsertedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
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
