CREATE OR ALTER PROCEDURE [$SchemaName$].SpUpsertSeries
  @Name NVARCHAR(128),
  @Unit NVARCHAR(32),
  @AggKind NVARCHAR(16),
  @Description NVARCHAR(512),
  @Service NVARCHAR(64),
  @InstanceId UNIQUEIDENTIFIER,
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
  USING (SELECT @MetricDefId AS MetricDefId, @Service AS Service, @InstanceId AS InstanceId, @TagHash AS TagHash) AS S
    ON (T.MetricDefId = S.MetricDefId AND T.Service = S.Service AND T.InstanceId = S.InstanceId AND T.TagHash = S.TagHash)
  WHEN MATCHED THEN
    UPDATE SET TagsJson = @TagsJson
  WHEN NOT MATCHED THEN
    INSERT (MetricDefId, Service, InstanceId, TagsJson, TagHash)
    VALUES(@MetricDefId, @Service, @InstanceId, @TagsJson, @TagHash);

  SELECT @SeriesId = SeriesId FROM [$SchemaName$].MetricSeries
  WHERE MetricDefId = @MetricDefId AND Service = @Service AND InstanceId = @InstanceId AND TagHash = @TagHash;
END
GO

CREATE OR ALTER PROCEDURE [$SchemaName$].SpUpsertMetricPointMinute
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
  DECLARE @ResourceName NVARCHAR(255) = CONCAT('infra:mpm:', @SeriesId, ':', CONVERT(VARCHAR(19), @BucketStartUtc, 126), ':', @BucketSecs);

  EXEC @LockRes = sp_getapplock
    @Resource = @ResourceName,
    @LockMode = 'Exclusive',
    @LockTimeout = 5000,
    @DbPrincipal = 'public';

  IF @LockRes < 0 RETURN;

  IF EXISTS (SELECT 1 FROM [$SchemaName$].MetricPointMinute WITH (UPDLOCK, HOLDLOCK)
             WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs)
  BEGIN
    UPDATE [$SchemaName$].MetricPointMinute
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
    INSERT INTO [$SchemaName$].MetricPointMinute(SeriesId, BucketStartUtc, BucketSecs,
      ValueSum, ValueCount, ValueMin, ValueMax, ValueLast, P50, P95, P99)
    VALUES(@SeriesId, @BucketStartUtc, @BucketSecs,
      @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast, @P50, @P95, @P99);
  END

  EXEC sp_releaseapplock @Resource = @ResourceName, @DbPrincipal='public';
END
GO
