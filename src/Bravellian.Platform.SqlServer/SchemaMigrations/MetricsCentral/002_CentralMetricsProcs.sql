CREATE OR ALTER PROCEDURE [$SchemaName$].SpUpsertSeriesCentral
  @Name NVARCHAR(128),
  @Unit NVARCHAR(32),
  @AggKind NVARCHAR(16),
  @Description NVARCHAR(512),
  @DatabaseId UNIQUEIDENTIFIER,
  @Service NVARCHAR(64),
  @TagsJson NVARCHAR(1024),
  @TagHash VARBINARY(32),
  @SeriesId BIGINT OUTPUT
AS
BEGIN
  SET NOCOUNT ON;

  DECLARE @MetricDefId INT;
  SELECT @MetricDefId = MetricDefId FROM [$SchemaName$].MetricDef WHERE Name = @Name;
  IF @MetricDefId IS NULL
  BEGIN
    INSERT INTO [$SchemaName$].MetricDef(Name, Unit, AggKind, Description)
    VALUES(@Name, @Unit, @AggKind, @Description);
    SET @MetricDefId = SCOPE_IDENTITY();
  END

  MERGE [$SchemaName$].MetricSeries WITH (HOLDLOCK) AS T
  USING (SELECT @MetricDefId AS MetricDefId, @DatabaseId AS DatabaseId, @Service AS Service, @TagHash AS TagHash) AS S
    ON (T.MetricDefId = S.MetricDefId AND T.DatabaseId = S.DatabaseId AND T.Service = S.Service AND T.TagHash = S.TagHash)
  WHEN MATCHED THEN
    UPDATE SET TagsJson = @TagsJson
  WHEN NOT MATCHED THEN
    INSERT (MetricDefId, DatabaseId, Service, TagsJson, TagHash)
    VALUES(@MetricDefId, @DatabaseId, @Service, @TagsJson, @TagHash);

  SELECT @SeriesId = SeriesId FROM [$SchemaName$].MetricSeries
  WHERE MetricDefId = @MetricDefId AND DatabaseId = @DatabaseId AND Service = @Service AND TagHash = @TagHash;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].SpUpsertMetricPointHourly
  @SeriesId BIGINT,
  @BucketStartUtc DATETIMEOFFSET(0),
  @BucketSecs SMALLINT,
  @ValueSum FLOAT,
  @ValueCount INT,
  @ValueMin FLOAT,
  @ValueMax FLOAT,
  @ValueLast FLOAT,
  @P50 FLOAT = NULL,
  @P95 FLOAT = NULL,
  @P99 FLOAT = NULL
AS
BEGIN
  SET NOCOUNT ON;

  DECLARE @LockRes INT;
  DECLARE @ResourceName NVARCHAR(255) = CONCAT('infra:mph:', @SeriesId, ':', CONVERT(VARCHAR(19), @BucketStartUtc, 126), ':', @BucketSecs);

  EXEC @LockRes = sp_getapplock
    @Resource = @ResourceName,
    @LockMode = 'Exclusive',
    @LockTimeout = 5000,
    @DbPrincipal = 'public';

  IF @LockRes < 0 RETURN;

  IF EXISTS (SELECT 1 FROM [$SchemaName$].MetricPointHourly WITH (UPDLOCK, HOLDLOCK)
             WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs)
  BEGIN
    UPDATE [$SchemaName$].MetricPointHourly
      SET ValueSum   = ISNULL(ValueSum,0)   + ISNULL(@ValueSum,0),
          ValueCount = ISNULL(ValueCount,0) + ISNULL(@ValueCount,0),
          ValueMin   = CASE WHEN ValueMin IS NULL OR @ValueMin < ValueMin THEN @ValueMin ELSE ValueMin END,
          ValueMax   = CASE WHEN ValueMax IS NULL OR @ValueMax > ValueMax THEN @ValueMax ELSE ValueMax END,
          ValueLast  = @ValueLast,
          InsertedUtc = SYSDATETIMEOFFSET()
    WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs;
  END
  ELSE
  BEGIN
    INSERT INTO [$SchemaName$].MetricPointHourly(SeriesId, BucketStartUtc, BucketSecs,
      ValueSum, ValueCount, ValueMin, ValueMax, ValueLast, P50, P95, P99)
    VALUES(@SeriesId, @BucketStartUtc, @BucketSecs,
      @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast, @P50, @P95, @P99);
  END

  EXEC sp_releaseapplock @Resource = @ResourceName, @DbPrincipal='public';
END
GO
