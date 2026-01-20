CREATE   PROCEDURE [infra].[SpUpsertSeries]
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

  SELECT @MetricDefId = MetricDefId FROM [infra].[MetricDef] WHERE Name = @Name;
  IF @MetricDefId IS NULL
  BEGIN
    INSERT INTO [infra].[MetricDef](Name, Unit, AggKind, Description)
    VALUES(@Name, @Unit, @AggKind, @Description);
    SET @MetricDefId = SCOPE_IDENTITY();
  END

  MERGE [infra].[MetricSeries] WITH (HOLDLOCK) AS T
  USING (SELECT @MetricDefId AS MetricDefId, @Service AS Service, @InstanceId AS InstanceId, @TagHash AS TagHash) AS S
    ON (T.MetricDefId = S.MetricDefId AND T.Service = S.Service AND T.InstanceId = S.InstanceId AND T.TagHash = S.TagHash)
  WHEN MATCHED THEN
    UPDATE SET TagsJson = @TagsJson
  WHEN NOT MATCHED THEN
    INSERT (MetricDefId, Service, InstanceId, TagsJson, TagHash)
    VALUES(@MetricDefId, @Service, @InstanceId, @TagsJson, @TagHash);

  SELECT @SeriesId = SeriesId FROM [infra].[MetricSeries]
  WHERE MetricDefId = @MetricDefId AND Service = @Service AND InstanceId = @InstanceId AND TagHash = @TagHash;
END