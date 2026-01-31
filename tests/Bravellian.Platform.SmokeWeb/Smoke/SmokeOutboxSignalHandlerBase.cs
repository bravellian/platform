using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal abstract class SmokeOutboxSignalHandlerBase : IOutboxHandler
{
    private readonly SmokeTestSignals signals;

    protected SmokeOutboxSignalHandlerBase(SmokeTestSignals signals)
    {
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
    }

    public abstract string Topic { get; }

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SmokePayload>(message.Payload);
        if (payload != null)
        {
            signals.Signal(payload.RunId, payload.Step, $"Processed {Topic} outbox message.");
        }

        return Task.CompletedTask;
    }
}
