using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutSliceHandler : IOutboxHandler
{
    private readonly SmokeTestState state;
    private readonly SmokeTestSignals signals;
    private readonly SmokeFanoutRepositories repositories;
    private readonly TimeProvider timeProvider;
    private readonly string topic;

    public SmokeFanoutSliceHandler(
        SmokeTestState state,
        SmokeTestSignals signals,
        SmokeFanoutRepositories repositories,
        TimeProvider timeProvider,
        string topic)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        this.repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.topic = topic ?? throw new ArgumentNullException(nameof(topic));
    }

    public string Topic => topic;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var slice = JsonSerializer.Deserialize<FanoutSlice>(message.Payload);
        if (slice == null)
        {
            return;
        }

        var (_, cursorRepository) = await repositories.GetAsync(cancellationToken).ConfigureAwait(false);

        await cursorRepository.MarkCompletedAsync(
            slice.fanoutTopic,
            slice.workKey,
            slice.shardKey,
            timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        var runId = state.ActiveRunId;
        if (!string.IsNullOrWhiteSpace(runId))
        {
            var stepName = SmokeFanoutDefaults.ResolveStepName(slice.workKey);
            if (!string.IsNullOrWhiteSpace(stepName))
            {
                signals.Signal(runId, stepName, $"Processed fanout slice ({slice.workKey}/{slice.shardKey}).");
            }
        }
    }
}
