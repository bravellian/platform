using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeFanoutJobHandler : IOutboxHandler
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<SmokeFanoutJobHandler> logger;

    public SmokeFanoutJobHandler(IServiceProvider serviceProvider, ILogger<SmokeFanoutJobHandler> logger)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Topic => SmokeFanoutDefaults.JobTopic;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SmokeFanoutJobPayload>(message.Payload);
        if (payload == null)
        {
            logger.LogWarning("Smoke fanout job payload missing for message {MessageId}", message.Id);
            return;
        }

        var key = SmokeFanoutDefaults.CoordinatorKey(payload.WorkKey ?? string.Empty);
        using var scope = serviceProvider.CreateScope();
        var coordinator = scope.ServiceProvider.GetKeyedService<IFanoutCoordinator>(key);
        if (coordinator == null)
        {
            logger.LogWarning("No fanout coordinator registered for key {Key}", key);
            return;
        }

        var processed = await coordinator.RunAsync(payload.FanoutTopic, payload.WorkKey, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Smoke fanout job dispatched {Count} slice(s) for {FanoutTopic}:{WorkKey}", processed, payload.FanoutTopic, payload.WorkKey);
    }
}
