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
public sealed class PostgresOperationTrackerTests : PostgresTestBase
{
    private PostgresOperationTracker? tracker;
    private PostgresOperationOptions options = new()
    {
        ConnectionString = string.Empty,
        SchemaName = "infra",
        OperationsTable = "Operations",
        OperationEventsTable = "OperationEvents",
    };
    private FakeTimeProvider timeProvider = default!;
    private string operationsTable = string.Empty;
    private string eventsTable = string.Empty;

    public PostgresOperationTrackerTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
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
        eventsTable = PostgresSqlHelper.Qualify(options.SchemaName, options.OperationEventsTable);
        var logger = new TestLogger<PostgresOperationTracker>(TestOutputHelper);
        tracker = new PostgresOperationTracker(Options.Create(options), timeProvider, logger);
    }

    /// <summary>When StartAsync is called, then a new operation row is created.</summary>
    /// <intent>Verify operations are persisted with pending status.</intent>
    /// <scenario>Given a tracker and a new operation name.</scenario>
    /// <behavior>The operations table contains the new operation with status Pending.</behavior>
    [Fact]
    public async Task StartAsync_CreatesOperationRow()
    {
        var operationId = await tracker!.StartAsync(
            "Test Operation",
            correlationContext: null,
            parentOperationId: null,
            tags: null,
            cancellationToken: CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var row = await connection.QuerySingleAsync(
            $"""
            SELECT "OperationId", "Status", "Name"
            FROM {operationsTable}
            WHERE "OperationId" = @OperationId
            """,
            new { OperationId = operationId.Value });

        ((string)row.OperationId).ShouldBe(operationId.Value);
        ((short)row.Status).ShouldBe((short)OperationStatus.Pending);
        ((string)row.Name).ShouldBe("Test Operation");
    }

    /// <summary>When AddEventAsync is called, then an operation event row is created.</summary>
    /// <intent>Verify operation events are persisted.</intent>
    /// <scenario>Given an existing operation and a new event.</scenario>
    /// <behavior>The operation events table contains the new event.</behavior>
    [Fact]
    public async Task AddEventAsync_CreatesOperationEvent()
    {
        var operationId = await tracker!.StartAsync(
            "Event Operation",
            correlationContext: null,
            parentOperationId: null,
            tags: null,
            cancellationToken: CancellationToken.None);

        await tracker.AddEventAsync(
            operationId,
            "Info",
            "Event message",
            dataJson: "{\"value\":1}",
            cancellationToken: CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var count = await connection.QuerySingleAsync<int>(
            $"""
            SELECT COUNT(*)
            FROM {eventsTable}
            WHERE "OperationId" = @OperationId
            """,
            new { OperationId = operationId.Value });

        count.ShouldBe(1);
    }
}

