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

using Microsoft.Data.SqlClient;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class ControlPlaneSchemaBundleTests
{
    private readonly SqlServerCollectionFixture fixture;

    public ControlPlaneSchemaBundleTests(SqlServerCollectionFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task TenantBundle_DoesNotInclude_ControlPlaneSchema()
    {
        var tenantConnection = await fixture.CreateTestDatabaseAsync("tenant-bundle");

        await DatabaseSchemaManager.ApplyTenantBundleAsync(tenantConnection, "app");

        (await TableExistsAsync(tenantConnection, "app", "Outbox")).ShouldBeTrue();
        (await TableExistsAsync(tenantConnection, "app", "Inbox")).ShouldBeTrue();
        (await TableExistsAsync(tenantConnection, "app", "Jobs")).ShouldBeTrue();

        (await TableExistsAsync(tenantConnection, "app", "MetricDef")).ShouldBeFalse();
    }

    [Fact]
    public async Task ControlPlaneBundle_AddsControlPlaneSchema_OnTopOfTenantBundle()
    {
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("control-bundle");

        await DatabaseSchemaManager.ApplyTenantBundleAsync(controlPlaneConnection, "control");
        await DatabaseSchemaManager.ApplyControlPlaneBundleAsync(controlPlaneConnection, "control");

        (await TableExistsAsync(controlPlaneConnection, "control", "Outbox")).ShouldBeTrue();
        (await TableExistsAsync(controlPlaneConnection, "control", "Inbox")).ShouldBeTrue();
        (await TableExistsAsync(controlPlaneConnection, "control", "Jobs")).ShouldBeTrue();

        (await TableExistsAsync(controlPlaneConnection, "control", "MetricDef")).ShouldBeTrue();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schemaName, string tableName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN OBJECT_ID(@FullName, 'U') IS NULL THEN 0 ELSE 1 END
            """;
        command.Parameters.AddWithValue("@FullName", $"[{schemaName}].[{tableName}]");
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
