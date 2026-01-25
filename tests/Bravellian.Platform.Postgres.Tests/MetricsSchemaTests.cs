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
using Npgsql;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Integration tests for metrics database schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MetricsSchemaTests : PostgresTestBase
{
    public MetricsSchemaTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture sharedFixture)
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
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Xunit.TestContext.Current.CancellationToken);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricDef'");

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task MetricSeries_Table_Should_Exist()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Xunit.TestContext.Current.CancellationToken);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricSeries'");

        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task MetricPointMinute_Table_Should_Exist()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Xunit.TestContext.Current.CancellationToken);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'infra' AND TABLE_NAME = 'MetricPointMinute'");

        Assert.Equal(1, exists);
    }

}

