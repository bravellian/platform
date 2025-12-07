CREATE   PROCEDURE [infra].[SpUpsertMetricPointHourly]
  @SeriesId BIGINT,
  @BucketStartUtc DATETIMEOFFSET(0),
  @BucketSecs INT,
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

  IF EXISTS (SELECT 1 FROM [infra].[MetricPointHourly] WITH (UPDLOCK, HOLDLOCK)
             WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc AND BucketSecs = @BucketSecs)
  BEGIN
    -- Do not update percentiles on merge; percentiles cannot be accurately combined
    UPDATE [infra].[MetricPointHourly]
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
    INSERT INTO [infra].[MetricPointHourly](SeriesId, BucketStartUtc, BucketSecs,
      ValueSum, ValueCount, ValueMin, ValueMax, ValueLast, P50, P95, P99)
    VALUES(@SeriesId, @BucketStartUtc, @BucketSecs,
      @ValueSum, @ValueCount, @ValueMin, @ValueMax, @ValueLast, @P50, @P95, @P99);
  END

  EXEC sp_releaseapplock @Resource = @ResourceName, @DbPrincipal='public';
END