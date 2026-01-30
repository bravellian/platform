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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests.TestUtilities;

internal sealed class SqlOutboxStoreBehaviorHarness : SqlServerTestBase, IOutboxStoreBehaviorHarness
{
    private readonly SqlOutboxOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        TableName = "Outbox",
        EnableSchemaDeployment = false,
    };

    private SqlOutboxStore? store;
    private SqlOutboxService? outbox;
    private FakeTimeProvider timeProvider = default!;

    public SqlOutboxStoreBehaviorHarness(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
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

        outbox = new SqlOutboxService(Options.Create(options), NullLogger<SqlOutboxService>.Instance);
        store = new SqlOutboxStore(Options.Create(options), timeProvider, NullLogger<SqlOutboxStore>.Instance);
    }

    public async Task ResetAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteAsync($"DELETE FROM [{options.SchemaName}].[{options.TableName}]");
    }
}
