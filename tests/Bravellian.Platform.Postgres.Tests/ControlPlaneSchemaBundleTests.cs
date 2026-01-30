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

using Npgsql;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class ControlPlaneSchemaBundleTests
{
    private readonly PostgresCollectionFixture fixture;

    public ControlPlaneSchemaBundleTests(PostgresCollectionFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task TenantBundle_DoesNotInclude_ControlPlaneSchema()
    {
        var tenantConnection = await fixture.CreateTestDatabaseAsync("tenant-bundle").ConfigureAwait(false);

        await DatabaseSchemaManager.ApplyTenantBundleAsync(tenantConnection, "app").ConfigureAwait(false);

        (await TableExistsAsync(tenantConnection, "app", "Outbox").ConfigureAwait(false)).ShouldBeTrue();
        (await TableExistsAsync(tenantConnection, "app", "Inbox").ConfigureAwait(false)).ShouldBeTrue();
        (await TableExistsAsync(tenantConnection, "app", "Jobs").ConfigureAwait(false)).ShouldBeTrue();

        (await TableExistsAsync(tenantConnection, "app", "Semaphore").ConfigureAwait(false)).ShouldBeFalse();
        (await TableExistsAsync(tenantConnection, "app", "MetricDef").ConfigureAwait(false)).ShouldBeFalse();
    }

    [Fact]
    public async Task ControlPlaneBundle_AddsControlPlaneSchema_OnTopOfTenantBundle()
    {
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("control-bundle").ConfigureAwait(false);

        await DatabaseSchemaManager.ApplyTenantBundleAsync(controlPlaneConnection, "control").ConfigureAwait(false);
        await DatabaseSchemaManager.ApplyControlPlaneBundleAsync(controlPlaneConnection, "control").ConfigureAwait(false);

        (await TableExistsAsync(controlPlaneConnection, "control", "Outbox").ConfigureAwait(false)).ShouldBeTrue();
        (await TableExistsAsync(controlPlaneConnection, "control", "Inbox").ConfigureAwait(false)).ShouldBeTrue();
        (await TableExistsAsync(controlPlaneConnection, "control", "Jobs").ConfigureAwait(false)).ShouldBeTrue();

        (await TableExistsAsync(controlPlaneConnection, "control", "Semaphore").ConfigureAwait(false)).ShouldBeTrue();
        (await TableExistsAsync(controlPlaneConnection, "control", "MetricDef").ConfigureAwait(false)).ShouldBeTrue();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schemaName, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@fullName)";
        command.Parameters.AddWithValue("fullName", $"\"{schemaName}\".\"{tableName}\"");
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }
}
