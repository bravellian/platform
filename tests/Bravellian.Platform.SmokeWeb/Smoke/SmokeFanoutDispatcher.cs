using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutDispatcher : IFanoutDispatcher
{
    private readonly SmokePlatformClientResolver platformClients;

    public SmokeFanoutDispatcher(SmokePlatformClientResolver platformClients)
    {
        this.platformClients = platformClients ?? throw new ArgumentNullException(nameof(platformClients));
    }

    public async Task<int> DispatchAsync(IEnumerable<FanoutSlice> slices, CancellationToken ct)
    {
        var outbox = await platformClients.GetOutboxAsync(ct).ConfigureAwait(false);
        var count = 0;

        foreach (var slice in slices)
        {
            var topic = $"fanout:{slice.fanoutTopic}:{slice.workKey}";
            var payload = JsonSerializer.Serialize(slice);

            await outbox.EnqueueAsync(topic, payload, slice.correlationId, ct).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
