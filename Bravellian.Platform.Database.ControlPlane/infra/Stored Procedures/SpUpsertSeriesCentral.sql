CREATE   PROCEDURE [infra].[SpUpsertSeriesCentral]
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

  SELECT @MetricDefId = MetricDefId FROM [infra].[MetricDef] WHERE Name = @Name;
  IF @MetricDefId IS NULL
  BEGIN
    INSERT INTO [infra].[MetricDef](Name, Unit, AggKind, Description)
    VALUES(@Name, @Unit, @AggKind, @Description);
    SET @MetricDefId = SCOPE_IDENTITY();
  END

  MERGE [infra].[MetricSeries] WITH (HOLDLOCK) AS T
  USING (SELECT @MetricDefId AS MetricDefId, @DatabaseId AS DatabaseId, @Service AS Service, @TagHash AS TagHash) AS S
    ON (T.MetricDefId = S.MetricDefId AND T.DatabaseId = S.DatabaseId AND T.Service = S.Service AND T.TagHash = S.TagHash)
  WHEN MATCHED THEN
    UPDATE SET TagsJson = @TagsJson
  WHEN NOT MATCHED THEN
    INSERT (MetricDefId, DatabaseId, Service, TagsJson, TagHash)
    VALUES(@MetricDefId, @DatabaseId, @Service, @TagsJson, @TagHash);

  SELECT @SeriesId = SeriesId FROM [infra].[MetricSeries]
  WHERE MetricDefId = @MetricDefId AND DatabaseId = @DatabaseId AND Service = @Service AND TagHash = @TagHash;
END