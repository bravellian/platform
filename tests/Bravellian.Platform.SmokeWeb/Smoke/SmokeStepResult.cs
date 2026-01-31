namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeStepResult
{
    public SmokeStepResult(string name)
    {
        Name = name;
        Status = SmokeStepStatus.Pending;
    }

    public string Name { get; }

    public SmokeStepStatus Status { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? Message { get; private set; }

    internal void MarkRunning(DateTimeOffset startedAt)
    {
        Status = SmokeStepStatus.Running;
        StartedAtUtc = startedAt;
        CompletedAtUtc = null;
        Message = null;
    }

    internal void MarkSucceeded(DateTimeOffset completedAt, string? message)
    {
        Status = SmokeStepStatus.Succeeded;
        CompletedAtUtc = completedAt;
        Message = message;
    }

    internal void MarkFailed(DateTimeOffset completedAt, string? message)
    {
        Status = SmokeStepStatus.Failed;
        CompletedAtUtc = completedAt;
        Message = message;
    }
}
