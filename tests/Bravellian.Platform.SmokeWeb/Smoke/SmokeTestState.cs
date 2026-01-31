namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeTestState
{
    private readonly Lock gate = new();
    private SmokeRun? currentRun;
    private SmokeRun? lastRun;

    public SmokeRun? CurrentRun
    {
        get
        {
            lock (gate)
            {
                return currentRun;
            }
        }
    }

    public SmokeRun? LastRun
    {
        get
        {
            lock (gate)
            {
                return lastRun;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return currentRun is { IsCompleted: false };
            }
        }
    }

    public string? GetActiveRunId()
    {
        lock (gate)
        {
            return currentRun?.RunId;
        }
    }

    public SmokeRun StartRun(string provider, DateTimeOffset startedAtUtc)
    {
        lock (gate)
        {
            if (currentRun is { IsCompleted: false })
            {
                return currentRun;
            }

            var run = SmokeRun.Create(provider, startedAtUtc);
            currentRun = run;
            lastRun = run;
            return run;
        }
    }

    public void MarkRunCompleted(SmokeRun run, DateTimeOffset completedAtUtc)
    {
        lock (gate)
        {
            if (!ReferenceEquals(currentRun, run))
            {
                return;
            }

            run.MarkCompleted(completedAtUtc);
            currentRun = null;
        }
    }

    public void MarkStepRunning(SmokeRun run, string stepName, DateTimeOffset startedAtUtc)
    {
        lock (gate)
        {
            if (!run.Steps.TryGetValue(stepName, out var step))
            {
                return;
            }

            step.MarkRunning(startedAtUtc);
        }
    }

    public void MarkStepSucceeded(SmokeRun run, string stepName, DateTimeOffset completedAtUtc, string? message)
    {
        lock (gate)
        {
            if (!run.Steps.TryGetValue(stepName, out var step))
            {
                return;
            }

            step.MarkSucceeded(completedAtUtc, message);
        }
    }

    public void MarkStepFailed(SmokeRun run, string stepName, DateTimeOffset completedAtUtc, string? message)
    {
        lock (gate)
        {
            if (!run.Steps.TryGetValue(stepName, out var step))
            {
                return;
            }

            step.MarkFailed(completedAtUtc, message);
        }
    }

    public SmokeStatusSnapshot GetStatusSnapshot(string provider)
    {
        lock (gate)
        {
            return new SmokeStatusSnapshot(
                provider,
                currentRun is { IsCompleted: false },
                currentRun?.ToSnapshot(),
                lastRun?.ToSnapshot());
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            currentRun = null;
            lastRun = null;
        }
    }
}
