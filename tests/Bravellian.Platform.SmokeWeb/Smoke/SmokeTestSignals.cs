using System.Collections.Concurrent;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeTestSignals
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SmokeSignal>> signals = new(StringComparer.Ordinal);

    public async Task<SmokeSignal> WaitAsync(string runId, string stepName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var key = BuildKey(runId, stepName);
        var tcs = signals.GetOrAdd(key, _ => new TaskCompletionSource<SmokeSignal>(TaskCreationOptions.RunContinuationsAsynchronously));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var signal = await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            signals.TryRemove(key, out _);
            return signal;
        }
        catch (OperationCanceledException)
        {
            signals.TryRemove(key, out _);
            return SmokeSignal.Timeout(stepName);
        }
    }

    public void Signal(string runId, string stepName, string? message)
    {
        var key = BuildKey(runId, stepName);
        var tcs = signals.GetOrAdd(key, _ => new TaskCompletionSource<SmokeSignal>(TaskCreationOptions.RunContinuationsAsynchronously));
        tcs.TrySetResult(SmokeSignal.Success(stepName, message));
        signals.TryRemove(key, out _);
    }

    private static string BuildKey(string runId, string stepName)
    {
        return string.Concat(runId, ":", stepName);
    }
}
