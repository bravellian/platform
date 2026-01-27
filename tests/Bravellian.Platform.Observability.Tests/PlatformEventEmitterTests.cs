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

using System.Text.Json;
using Bravellian.Platform.Audit;
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Operations;
using Shouldly;

namespace Bravellian.Platform.Observability.Tests;

public sealed class PlatformEventEmitterTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmitOperationStartedUsesTrackerAndWriter()
    {
        var tracker = new FakeOperationTracker(new OperationId("op-1"));
        var writer = new FakeAuditWriter();
        var accessor = new AmbientCorrelationContextAccessor
        {
            Current = new CorrelationContext(
                new CorrelationId("corr-1"),
                null,
                "trace-1",
                "span-1",
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        };
        var timeProvider = new FixedTimeProvider(FixedNow);

        var emitter = new PlatformEventEmitter(writer, tracker, accessor, timeProvider);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [PlatformTagKeys.Tenant] = "tenant-1" };

        var operationId = await emitter.EmitOperationStartedAsync(
            "Import",
            correlationContext: null,
            parentOperationId: null,
            tags: tags,
            cancellationToken: CancellationToken.None);

        operationId.Value.ShouldBe("op-1");
        tracker.LastStartName.ShouldBe("Import");
        tracker.LastStartCorrelation.ShouldNotBeNull();
        tracker.LastStartCorrelation!.CorrelationId.Value.ShouldBe("corr-1");

        writer.Events.Count.ShouldBe(1);
        var auditEvent = writer.Events[0];
        auditEvent.Name.ShouldBe(PlatformEventNames.OperationStarted);
        auditEvent.Correlation.ShouldNotBeNull();
        auditEvent.Correlation!.CorrelationId.Value.ShouldBe("corr-1");
        auditEvent.Anchors[0].AnchorId.ShouldBe("op-1");
        auditEvent.OccurredAtUtc.ShouldBe(FixedNow);

        auditEvent.DataJson.ShouldNotBeNull();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(auditEvent.DataJson!);
        payload.ShouldNotBeNull();
        payload!.ContainsKey("tags").ShouldBeTrue();
    }

    [Fact]
    public async Task EmitOperationCompletedMarksFailure()
    {
        var tracker = new FakeOperationTracker(new OperationId("op-2"));
        var writer = new FakeAuditWriter();
        var timeProvider = new FixedTimeProvider(FixedNow);
        var emitter = new PlatformEventEmitter(writer, tracker, null, timeProvider);

        await emitter.EmitOperationCompletedAsync(
            new OperationId("op-2"),
            OperationStatus.Failed,
            "boom",
            null,
            null,
            CancellationToken.None);

        tracker.LastCompleteStatus.ShouldBe(OperationStatus.Failed);
        writer.Events.Count.ShouldBe(1);
        writer.Events[0].Name.ShouldBe(PlatformEventNames.OperationFailed);
        writer.Events[0].Outcome.ShouldBe(EventOutcome.Failure);
        writer.Events[0].OccurredAtUtc.ShouldBe(FixedNow);
    }

    private sealed class FakeOperationTracker : IOperationTracker
    {
        private readonly OperationId operationId;

        public FakeOperationTracker(OperationId operationId)
        {
            this.operationId = operationId;
        }

        public string? LastStartName { get; private set; }

        public CorrelationContext? LastStartCorrelation { get; private set; }

        public OperationStatus? LastCompleteStatus { get; private set; }

        public Task<OperationId> StartAsync(
            string name,
            CorrelationContext? correlationContext,
            OperationId? parentOperationId,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            LastStartName = name;
            LastStartCorrelation = correlationContext;
            return Task.FromResult(operationId);
        }

        public Task UpdateProgressAsync(OperationId operationId, double? percentComplete, string? message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task AddEventAsync(OperationId operationId, string kind, string message, string? dataJson, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CompleteAsync(OperationId operationId, OperationStatus status, string? message, CancellationToken cancellationToken)
        {
            LastCompleteStatus = status;
            return Task.CompletedTask;
        }

        public Task<OperationSnapshot?> GetSnapshotAsync(OperationId operationId, CancellationToken cancellationToken)
        {
            return Task.FromResult<OperationSnapshot?>(null);
        }
    }

    private sealed class FakeAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset fixedUtcNow;

        public FixedTimeProvider(DateTimeOffset fixedUtcNow)
        {
            this.fixedUtcNow = fixedUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }
}
