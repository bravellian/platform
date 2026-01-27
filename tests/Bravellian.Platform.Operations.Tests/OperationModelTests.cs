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
using Shouldly;

namespace Bravellian.Platform.Operations.Tests;

public sealed class OperationModelTests
{
    [Fact]
    public async Task StartUpdateCompleteTransitions()
    {
        var tracker = new InMemoryOperationTracker();
        var correlation = new CorrelationContext(
            new CorrelationId("corr-ops"),
            null,
            null,
            null,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var operationId = await tracker.StartAsync(
            "Import",
            correlation,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tenant"] = "t-1" },
            CancellationToken.None);

        var started = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        started.ShouldNotBeNull();
        started!.Status.ShouldBe(OperationStatus.Pending);
        started.Correlation.ShouldBe(correlation);

        await tracker.UpdateProgressAsync(operationId, 25, "Loading", CancellationToken.None);

        var updated = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        updated.ShouldNotBeNull();
        updated!.Status.ShouldBe(OperationStatus.Running);
        updated.PercentComplete.ShouldBe(25);
        updated.Message.ShouldBe("Loading");

        await tracker.CompleteAsync(operationId, OperationStatus.Succeeded, "Done", CancellationToken.None);

        var completed = await tracker.GetSnapshotAsync(operationId, CancellationToken.None);
        completed.ShouldNotBeNull();
        completed!.Status.ShouldBe(OperationStatus.Succeeded);
        completed.CompletedAtUtc.ShouldNotBeNull();
        completed.Message.ShouldBe("Done");
    }

    [Fact]
    public async Task ScopeCompletesOnSuccess()
    {
        var tracker = new InMemoryOperationTracker();

        await using (var scope = await OperationScope.StartAsync(tracker, "ScopeSuccess", cancellationToken: Xunit.TestContext.Current.CancellationToken))
        {
            await tracker.UpdateProgressAsync(scope.OperationId, 50, "Halfway", CancellationToken.None);
        }

        var snapshot = await tracker.GetSnapshotAsync(tracker.LastStartedId, CancellationToken.None);
        snapshot.ShouldNotBeNull();
        snapshot!.Status.ShouldBe(OperationStatus.Succeeded);
    }

    [Fact]
    public async Task ScopeRecordsFailureOnDispose()
    {
        var tracker = new InMemoryOperationTracker();

        await using (var scope = await OperationScope.StartAsync(tracker, "ScopeFailure", cancellationToken: Xunit.TestContext.Current.CancellationToken))
        {
            scope.Fail(new InvalidOperationException("boom"));
        }

        var snapshot = await tracker.GetSnapshotAsync(tracker.LastStartedId, CancellationToken.None);
        snapshot.ShouldNotBeNull();
        snapshot!.Status.ShouldBe(OperationStatus.Failed);
        tracker.Events.ShouldContain(evt => evt.Kind == "Error" && evt.Message == "boom");
    }

    [Fact]
    public async Task RunAsyncCompletesAndPropagatesFailure()
    {
        var tracker = new InMemoryOperationTracker();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await OperationScope.RunAsync(tracker,
                "Runner",
                _ => throw new InvalidOperationException("fail"), cancellationToken: Xunit.TestContext.Current.CancellationToken);
        });

        var snapshot = await tracker.GetSnapshotAsync(tracker.LastStartedId, CancellationToken.None);
        snapshot.ShouldNotBeNull();
        snapshot!.Status.ShouldBe(OperationStatus.Failed);
    }

    [Fact]
    public async Task WatcherContractReturnsSnapshots()
    {
        var watcher = new FakeOperationWatcher();
        var threshold = TimeSpan.FromMinutes(10);

        var results = await watcher.FindStalledAsync(threshold, CancellationToken.None);

        results.ShouldNotBeNull();
        results.Count.ShouldBe(1);
        results[0].Status.ShouldBe(OperationStatus.Stalled);
    }

    private sealed class InMemoryOperationTracker : IOperationTracker
    {
        private readonly Dictionary<OperationId, OperationSnapshot> snapshots = new();

        public List<OperationEvent> Events { get; } = new();

        public OperationId LastStartedId { get; private set; }

        public Task<OperationId> StartAsync(
            string name,
            CorrelationContext? correlationContext,
            OperationId? parentOperationId,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            var now = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(snapshots.Count);
            var operationId = OperationId.NewId();
            var snapshot = new OperationSnapshot(
                operationId,
                name,
                OperationStatus.Pending,
                now,
                now,
                null,
                null,
                null,
                correlationContext,
                parentOperationId,
                tags);

            snapshots[operationId] = snapshot;
            LastStartedId = operationId;
            return Task.FromResult(operationId);
        }

        public Task UpdateProgressAsync(OperationId operationId, double? percentComplete, string? message, CancellationToken cancellationToken)
        {
            var snapshot = snapshots[operationId];
            var updated = new OperationSnapshot(
                snapshot.OperationId,
                snapshot.Name,
                OperationStatus.Running,
                snapshot.StartedAtUtc,
                snapshot.UpdatedAtUtc.AddMinutes(1),
                snapshot.CompletedAtUtc,
                percentComplete,
                message,
                snapshot.Correlation,
                snapshot.ParentOperationId,
                snapshot.Tags);

            snapshots[operationId] = updated;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(OperationId operationId, string kind, string message, string? dataJson, CancellationToken cancellationToken)
        {
            Events.Add(new OperationEvent(operationId, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), kind, message, dataJson));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(OperationId operationId, OperationStatus status, string? message, CancellationToken cancellationToken)
        {
            var snapshot = snapshots[operationId];
            var now = snapshot.UpdatedAtUtc.AddMinutes(1);
            snapshots[operationId] = new OperationSnapshot(
                snapshot.OperationId,
                snapshot.Name,
                status,
                snapshot.StartedAtUtc,
                now,
                now,
                snapshot.PercentComplete,
                message,
                snapshot.Correlation,
                snapshot.ParentOperationId,
                snapshot.Tags);

            return Task.CompletedTask;
        }

        public Task<OperationSnapshot?> GetSnapshotAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            snapshots.TryGetValue(operationId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeOperationWatcher : IOperationWatcher
    {
        public Task<IReadOnlyList<OperationSnapshot>> FindStalledAsync(TimeSpan threshold, CancellationToken cancellationToken)
        {
            var snapshot = new OperationSnapshot(
                new OperationId("op-stalled"),
                "Stalled",
                OperationStatus.Stalled,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

            return Task.FromResult<IReadOnlyList<OperationSnapshot>>(new[] { snapshot });
        }

        public Task MarkStalledAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
