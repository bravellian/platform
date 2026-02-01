using System.Collections.Concurrent;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeRun
{
    private readonly ConcurrentDictionary<string, SmokeStepResult> steps;

    private SmokeRun(string runId, string provider, DateTimeOffset startedAtUtc, IEnumerable<string> stepNames)
    {
        RunId = runId;
        Provider = provider;
        StartedAtUtc = startedAtUtc;
        steps = new ConcurrentDictionary<string, SmokeStepResult>(
            stepNames.Select(name => new KeyValuePair<string, SmokeStepResult>(name, new SmokeStepResult(name))),
            StringComparer.Ordinal);
    }

    public string RunId { get; }

    public string Provider { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public IReadOnlyDictionary<string, SmokeStepResult> Steps => steps;

    public bool IsCompleted => CompletedAtUtc.HasValue;

    internal static SmokeRun Create(string provider, DateTimeOffset startedAtUtc)
    {
        var stepNames = new[]
        {
            SmokeStepNames.Lease,
            SmokeStepNames.LeaseStorm,
            SmokeStepNames.Outbox,
            SmokeStepNames.Inbox,
            SmokeStepNames.Scheduler,
            SmokeStepNames.FanoutSmall,
            SmokeStepNames.FanoutBurst,
            SmokeStepNames.Idempotency,
            SmokeStepNames.Operations,
            SmokeStepNames.Audit,
        };

        return new SmokeRun(Guid.NewGuid().ToString("N"), provider, startedAtUtc, stepNames);
    }

    internal void MarkCompleted(DateTimeOffset completedAt)
    {
        CompletedAtUtc = completedAt;
    }

    internal SmokeRunSnapshot ToSnapshot()
    {
        var stepSnapshots = steps.Values
            .OrderBy(step => step.Name, StringComparer.Ordinal)
            .Select(step => new SmokeStepSnapshot(
                step.Name,
                step.Status.ToString(),
                step.StartedAtUtc,
                step.CompletedAtUtc,
                step.Message))
            .ToList();

        return new SmokeRunSnapshot(RunId, Provider, StartedAtUtc, CompletedAtUtc, stepSnapshots);
    }
}
