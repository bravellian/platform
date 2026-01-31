using Bravellian.Platform;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutCoordinator : IFanoutCoordinator
{
    private readonly IFanoutPlanner planner;
    private readonly IFanoutDispatcher dispatcher;
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly ILogger<SmokeFanoutCoordinator> logger;

    public SmokeFanoutCoordinator(
        IFanoutPlanner planner,
        IFanoutDispatcher dispatcher,
        ISystemLeaseFactory leaseFactory,
        ILogger<SmokeFanoutCoordinator> logger)
    {
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.leaseFactory = leaseFactory ?? throw new ArgumentNullException(nameof(leaseFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RunAsync(string fanoutTopic, string? workKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);

        var leaseName = workKey is null ? $"fanout:{fanoutTopic}" : $"fanout:{fanoutTopic}:{workKey}";
        var contextJson = $"{{\"fanoutTopic\":\"{fanoutTopic}\",\"workKey\":\"{workKey}\",\"machineName\":\"{Environment.MachineName}\"}}";

        var lease = await leaseFactory.AcquireAsync(
            leaseName,
            TimeSpan.FromSeconds(30),
            contextJson,
            cancellationToken: ct).ConfigureAwait(false);

        if (lease == null)
        {
            logger.LogDebug("Could not acquire lease for fanout {FanoutTopic}:{WorkKey}", fanoutTopic, workKey);
            return 0;
        }

        await using (lease.ConfigureAwait(false))
        {
            var slices = await planner.GetDueSlicesAsync(fanoutTopic, workKey, ct).ConfigureAwait(false);
            if (slices.Count == 0)
            {
                logger.LogDebug("No slices due for fanout {FanoutTopic}:{WorkKey}", fanoutTopic, workKey);
                return 0;
            }

            var dispatched = await dispatcher.DispatchAsync(slices, ct).ConfigureAwait(false);
            logger.LogInformation("Dispatched {Count} slices for fanout {FanoutTopic}:{WorkKey}", dispatched, fanoutTopic, workKey);
            return dispatched;
        }
    }
}
