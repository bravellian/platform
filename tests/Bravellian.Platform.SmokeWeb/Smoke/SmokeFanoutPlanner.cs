using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutPlanner : IFanoutPlanner
{
    private readonly SmokeFanoutRepositories repositories;
    private readonly SmokeTestState state;
    private readonly TimeProvider timeProvider;

    public SmokeFanoutPlanner(
        SmokeFanoutRepositories repositories,
        SmokeTestState state,
        TimeProvider timeProvider)
    {
        this.repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<FanoutSlice>> GetDueSlicesAsync(string fanoutTopic, string? workKey, CancellationToken ct)
    {
        if (!string.Equals(fanoutTopic, SmokeFanoutDefaults.FanoutTopic, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<FanoutSlice>();
        }

        if (workKey is not null && !string.Equals(workKey, SmokeFanoutDefaults.WorkKey, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<FanoutSlice>();
        }

        var (policyRepository, cursorRepository) = await repositories.GetAsync(ct).ConfigureAwait(false);
        var (everySeconds, jitterSeconds) = await policyRepository.GetCadenceAsync(
            SmokeFanoutDefaults.FanoutTopic,
            SmokeFanoutDefaults.WorkKey,
            ct).ConfigureAwait(false);

        var lastCompleted = await cursorRepository.GetLastAsync(
            SmokeFanoutDefaults.FanoutTopic,
            SmokeFanoutDefaults.WorkKey,
            SmokeFanoutDefaults.ShardKey,
            ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        _ = jitterSeconds;
        var spacing = TimeSpan.FromSeconds(Math.Max(0, everySeconds));

        if (lastCompleted is null || (now - lastCompleted) >= spacing)
        {
            var correlationId = state.GetActiveRunId();
            return new[]
            {
                new FanoutSlice(
                    SmokeFanoutDefaults.FanoutTopic,
                    SmokeFanoutDefaults.ShardKey,
                    SmokeFanoutDefaults.WorkKey,
                    windowStart: lastCompleted,
                    correlationId: correlationId),
            };
        }

        return Array.Empty<FanoutSlice>();
    }
}
