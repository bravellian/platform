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

using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Bravellian.Platform.Tests;

internal sealed class PostgresOutboxStoreBehaviorHarness : PostgresTestBase, IOutboxStoreBehaviorHarness
{
    private readonly PostgresOutboxOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox",
        EnableSchemaDeployment = false,
    };

    private PostgresOutboxStore? store;
    private PostgresOutboxService? outbox;
    private FakeTimeProvider timeProvider = default!;

    public PostgresOutboxStoreBehaviorHarness(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public IOutbox Outbox => outbox ?? throw new InvalidOperationException("Harness has not been initialized.");

    public IOutboxStore Store => store ?? throw new InvalidOperationException("Harness has not been initialized.");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        timeProvider = new FakeTimeProvider();
        options.ConnectionString = ConnectionString;

        outbox = new PostgresOutboxService(Options.Create(options), NullLogger<PostgresOutboxService>.Instance);
        store = new PostgresOutboxStore(Options.Create(options), timeProvider, NullLogger<PostgresOutboxStore>.Instance);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var tableName = PostgresSqlHelper.Qualify(options.SchemaName, options.TableName);
        await connection.ExecuteAsync($"DELETE FROM {tableName}");
    }
}
