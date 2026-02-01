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

    /// <summary>When tenant Bundle Does Not Include Control Plane Schema, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for tenant Bundle Does Not Include Control Plane Schema.</intent>
    /// <scenario>Given tenant Bundle Does Not Include Control Plane Schema.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When tenant Bundle Creates Fanout Tables, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for tenant Bundle Creates Fanout Tables.</intent>
    /// <scenario>Given tenant Bundle Creates Fanout Tables.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task TenantBundle_CreatesFanoutTables()
    {
        var tenantConnection = await fixture.CreateTestDatabaseAsync("tenant-fanout-bundle");

        await DatabaseSchemaManager.ApplyTenantBundleAsync(tenantConnection, "app");

        (await TableExistsAsync(tenantConnection, "app", "FanoutPolicy")).ShouldBeTrue();
        (await TableExistsAsync(tenantConnection, "app", "FanoutCursor")).ShouldBeTrue();
    }

    /// <summary>When control Plane Bundle Adds Control Plane Schema On Top Of Tenant Bundle, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for control Plane Bundle Adds Control Plane Schema On Top Of Tenant Bundle.</intent>
    /// <scenario>Given control Plane Bundle Adds Control Plane Schema On Top Of Tenant Bundle.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@fullName)";
        command.Parameters.AddWithValue("fullName", $"\"{schemaName}\".\"{tableName}\"");
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }
}
