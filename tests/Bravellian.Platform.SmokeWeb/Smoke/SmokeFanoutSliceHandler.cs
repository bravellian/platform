using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutSliceHandler : IOutboxHandler
{
    private readonly SmokeTestState state;
    private readonly SmokeTestSignals signals;
    private readonly SmokeFanoutRepositories repositories;
    private readonly TimeProvider timeProvider;

    public SmokeFanoutSliceHandler(
        SmokeTestState state,
        SmokeTestSignals signals,
        SmokeFanoutRepositories repositories,
        TimeProvider timeProvider)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        this.repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string Topic => SmokeFanoutDefaults.SliceTopic;

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

        var runId = state.GetActiveRunId();
        if (!string.IsNullOrWhiteSpace(runId))
        {
            signals.Signal(runId, SmokeStepNames.Fanout, "Processed fanout slice.");
        }
    }
}
