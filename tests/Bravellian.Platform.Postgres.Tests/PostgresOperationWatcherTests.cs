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

using Bravellian.Platform.Operations;
using Bravellian.Platform.Tests.TestUtilities;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class PostgresOperationWatcherTests : PostgresTestBase
{
    private PostgresOperationWatcher? watcher;
    private PostgresOperationOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        OperationsTable = "Operations",
        OperationEventsTable = "OperationEvents",
    };
    private FakeTimeProvider timeProvider = default!;
    private string operationsTable = string.Empty;

    public PostgresOperationWatcherTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureOperationsSchemaAsync(ConnectionString).ConfigureAwait(false);

        timeProvider = new FakeTimeProvider();
        options.ConnectionString = ConnectionString;
        operationsTable = PostgresSqlHelper.Qualify(options.SchemaName, options.OperationsTable);
        var logger = new TestLogger<PostgresOperationWatcher>(TestOutputHelper);
        watcher = new PostgresOperationWatcher(Options.Create(options), timeProvider, logger);
    }

    /// <summary>When an operation is stale, then FindStalledAsync returns it.</summary>
    /// <intent>Verify stalled detection returns pending/running operations that are past the threshold.</intent>
    /// <scenario>Given an operation with an old UpdatedAtUtc timestamp.</scenario>
    /// <behavior>The stalled list contains the operation.</behavior>
    [Fact]
    public async Task FindStalledAsync_ReturnsStalledOperations()
    {
        var operationId = OperationId.NewId();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddMinutes(-10);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {operationsTable}
            ("OperationId", "Name", "Status", "StartedAtUtc", "UpdatedAtUtc")
            VALUES (@OperationId, @Name, @Status, @StartedAtUtc, @UpdatedAtUtc)
            """,
            new
            {
                OperationId = operationId.Value,
                Name = "Stalled Operation",
                Status = (short)OperationStatus.Pending,
                StartedAtUtc = updatedAt,
                UpdatedAtUtc = updatedAt,
            });

        timeProvider.SetUtcNow(now);
        var results = await watcher!.FindStalledAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        results.ShouldContain(snapshot => snapshot.OperationId == operationId);
    }

    /// <summary>When MarkStalledAsync is called, then the operation is marked stalled.</summary>
    /// <intent>Verify stalled transitions update status and completion time.</intent>
    /// <scenario>Given an existing pending operation.</scenario>
    /// <behavior>Status is set to Stalled and CompletedAtUtc is populated.</behavior>
    [Fact]
    public async Task MarkStalledAsync_UpdatesOperationStatus()
    {
        var operationId = OperationId.NewId();
        var now = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO {operationsTable}
            ("OperationId", "Name", "Status", "StartedAtUtc", "UpdatedAtUtc")
            VALUES (@OperationId, @Name, @Status, @StartedAtUtc, @UpdatedAtUtc)
            """,
            new
            {
                OperationId = operationId.Value,
                Name = "Pending Operation",
                Status = (short)OperationStatus.Pending,
                StartedAtUtc = now,
                UpdatedAtUtc = now,
            });

        timeProvider.SetUtcNow(now.AddMinutes(1));

        await watcher!.MarkStalledAsync(operationId, CancellationToken.None);

        var row = await connection.QuerySingleAsync(
            $"""
            SELECT "Status", "CompletedAtUtc"
            FROM {operationsTable}
            WHERE "OperationId" = @OperationId
            """,
            new { OperationId = operationId.Value });

        ((short)row.Status).ShouldBe((short)OperationStatus.Stalled);
        ((DateTimeOffset?)row.CompletedAtUtc).ShouldNotBeNull();
    }
}
