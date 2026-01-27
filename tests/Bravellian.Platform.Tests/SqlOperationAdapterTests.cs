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

using Bravellian.Platform.Correlation;
using Bravellian.Platform.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SqlOperationAdapterTests : SqlServerTestBase
{
    public SqlOperationAdapterTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture sharedFixture)
        : base(testOutputHelper, sharedFixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ApplyScriptsAsync(SqlOperationSchemaScripts.GetScripts("infra", "Operations", "OperationEvents"))
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task StartUpdateCompleteRoundTrip()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new SqlOperationOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            OperationsTable = "Operations",
            OperationEventsTable = "OperationEvents",
        });

        var tracker = new SqlOperationTracker(options, timeProvider, NullLogger<SqlOperationTracker>.Instance);
        var correlation = new CorrelationContext(
            new CorrelationId("corr-ops"),
            null,
            "trace-ops",
            "span-ops",
            timeProvider.GetUtcNow());

        var operationId = await tracker.StartAsync(
            "Import",
            correlation,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tenant"] = "t-1" },
            CancellationToken.None);

        var started = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        started.ShouldNotBeNull();
        started!.Status.ShouldBe(OperationStatus.Pending);
        started.Correlation.ShouldNotBeNull();
        started.Correlation!.CorrelationId.Value.ShouldBe("corr-ops");

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await tracker.UpdateProgressAsync(operationId, 40, "Loading", CancellationToken.None);

        var updated = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        updated.ShouldNotBeNull();
        updated!.Status.ShouldBe(OperationStatus.Running);
        updated.PercentComplete.ShouldBe(40);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await tracker.CompleteAsync(operationId, OperationStatus.Succeeded, "Done", CancellationToken.None);

        var completed = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        completed.ShouldNotBeNull();
        completed!.Status.ShouldBe(OperationStatus.Succeeded);
        completed.CompletedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task WatcherFindsAndMarksStalled()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new SqlOperationOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            OperationsTable = "Operations",
            OperationEventsTable = "OperationEvents",
        });

        var tracker = new SqlOperationTracker(options, timeProvider, NullLogger<SqlOperationTracker>.Instance);
        var watcher = new SqlOperationWatcher(options, timeProvider, NullLogger<SqlOperationWatcher>.Instance);

        var operationId = await tracker.StartAsync("StallMe", null, null, null, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromHours(2));

        var stalled = await watcher.FindStalledAsync(TimeSpan.FromMinutes(30), CancellationToken.None);
        stalled.ShouldContain(snapshot => snapshot.OperationId == operationId);

        await watcher.MarkStalledAsync(operationId, CancellationToken.None);
        var snapshot = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        snapshot.ShouldNotBeNull();
        snapshot!.Status.ShouldBe(OperationStatus.Stalled);
    }

    private async Task ApplyScriptsAsync(IEnumerable<string> scripts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        foreach (var script in scripts)
        {
            await ExecuteSqlScriptAsync(connection, script).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteSqlScriptAsync(SqlConnection connection, string script)
    {
        var batches = script.Split(
            new[] { "\nGO\n", "\nGO\r\n", "\rGO\r", "\nGO", "GO\n" },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            await using var command = new SqlCommand(trimmed, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }
}

