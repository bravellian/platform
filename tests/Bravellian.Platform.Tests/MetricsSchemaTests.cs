// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.


using Dapper;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Integration tests for metrics database schema.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MetricsSchemaTests : SqlServerTestBase
{
    public MetricsSchemaTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture sharedFixture)
        : base(testOutputHelper, sharedFixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Setup metrics schema
        await DatabaseSchemaManager.EnsureMetricsSchemaAsync(ConnectionString).ConfigureAwait(false);
    }

    [Fact]
    public async Task MetricDef_Table_Should_Exist()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricDef'").ConfigureAwait(false);

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task MetricSeries_Table_Should_Exist()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricSeries'").ConfigureAwait(false);

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task MetricPointMinute_Table_Should_Exist()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricPointMinute'").ConfigureAwait(false);

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task SpUpsertSeries_Should_Create_Series()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var parameters = new DynamicParameters();
        parameters.Add("@Name", "test.metric");
        parameters.Add("@Unit", "count");
        parameters.Add("@AggKind", "counter");
        parameters.Add("@Description", "Test metric");
        parameters.Add("@Service", "TestService");
        parameters.Add("@InstanceId", Guid.NewGuid());
        parameters.Add("@TagsJson", "{}");
        parameters.Add("@TagHash", new byte[32]);
        parameters.Add("@SeriesId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync("[infra].[SpUpsertSeries]", parameters, commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

        var seriesId = parameters.Get<long>("@SeriesId");
        Assert.True(seriesId > 0);
    }

    [Fact(Skip = "TODO: Debug SP_GETAPPLOCK behavior in test environment")]
    public async Task SpUpsertMetricPointMinute_Should_Insert_And_Update()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // First, create a series
        var seriesParams = new DynamicParameters();
        seriesParams.Add("@Name", "test.counter");
        seriesParams.Add("@Unit", "count");
        seriesParams.Add("@AggKind", "counter");
        seriesParams.Add("@Description", "Test counter");
        seriesParams.Add("@Service", "TestService");
        seriesParams.Add("@InstanceId", Guid.NewGuid());
        seriesParams.Add("@TagsJson", "{}");
        seriesParams.Add("@TagHash", new byte[32]);
        seriesParams.Add("@SeriesId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync("[infra].[SpUpsertSeries]", seriesParams, commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);
        var seriesId = seriesParams.Get<long>("@SeriesId");

        // Use truncated datetime to match DATETIME2(0)
        var now = DateTime.UtcNow;
        var bucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

        // Insert first point
        var pointParams1 = new DynamicParameters();
        pointParams1.Add("@SeriesId", seriesId);
        pointParams1.Add("@BucketStartUtc", bucketStart);
        pointParams1.Add("@BucketSecs", 60);
        pointParams1.Add("@ValueSum", 10.0);
        pointParams1.Add("@ValueCount", 5);
        pointParams1.Add("@ValueMin", 1.0);
        pointParams1.Add("@ValueMax", 3.0);
        pointParams1.Add("@ValueLast", 2.5);

        await connection.ExecuteAsync("[infra].[SpUpsertMetricPointMinute]", pointParams1, commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

        // Update same point (additive)
        var pointParams2 = new DynamicParameters();
        pointParams2.Add("@SeriesId", seriesId);
        pointParams2.Add("@BucketStartUtc", bucketStart);
        pointParams2.Add("@BucketSecs", 60);
        pointParams2.Add("@ValueSum", 5.0);
        pointParams2.Add("@ValueCount", 3);
        pointParams2.Add("@ValueMin", 0.5);
        pointParams2.Add("@ValueMax", 2.0);
        pointParams2.Add("@ValueLast", 1.5);

        await connection.ExecuteAsync("[infra].[SpUpsertMetricPointMinute]", pointParams2, commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

        // Check if any rows exist
        var rowCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM [infra].[MetricPointMinute] WHERE SeriesId = @SeriesId",
            new { SeriesId = seriesId }).ConfigureAwait(false);

        Assert.True(rowCount > 0, "No rows were inserted into MetricPointMinute");

        // Verify additive update
        var result = await connection.QuerySingleAsync<dynamic>(
            "SELECT ValueSum, ValueCount, ValueMin, ValueMax, ValueLast FROM [infra].[MetricPointMinute] WHERE SeriesId = @SeriesId AND BucketStartUtc = @BucketStartUtc",
            new { SeriesId = seriesId, BucketStartUtc = bucketStart }).ConfigureAwait(false);

        Assert.Equal(15.0, (double)result.ValueSum, 0.01); // 10 + 5
        Assert.Equal(8, (int)result.ValueCount); // 5 + 3
        Assert.Equal(0.5, (double)result.ValueMin, 0.01); // min(1.0, 0.5)
        Assert.Equal(3.0, (double)result.ValueMax, 0.01); // max(3.0, 2.0)
        Assert.Equal(1.5, (double)result.ValueLast, 0.01); // last value wins
    }
}
