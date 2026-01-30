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
using Npgsql;

namespace Bravellian.Platform.Tests;

internal sealed class PostgresInboxWorkStoreBehaviorHarness : PostgresTestBase, IInboxWorkStoreBehaviorHarness
{
    private readonly PostgresInboxOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Inbox",
        EnableSchemaDeployment = false,
    };

    private PostgresInboxWorkStore? store;
    private PostgresInboxService? inbox;

    public PostgresInboxWorkStoreBehaviorHarness(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public IInbox Inbox => inbox ?? throw new InvalidOperationException("Harness has not been initialized.");

    public IInboxWorkStore WorkStore => store ?? throw new InvalidOperationException("Harness has not been initialized.");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        await DatabaseSchemaManager.EnsureInboxWorkQueueSchemaAsync(ConnectionString).ConfigureAwait(false);

        options.ConnectionString = ConnectionString;
        inbox = new PostgresInboxService(Options.Create(options), NullLogger<PostgresInboxService>.Instance);
        store = new PostgresInboxWorkStore(Options.Create(options), TimeProvider.System, NullLogger<PostgresInboxWorkStore>.Instance);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var tableName = PostgresSqlHelper.Qualify(options.SchemaName, options.TableName);
        await connection.ExecuteAsync($"DELETE FROM {tableName}");
    }
}
