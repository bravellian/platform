using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeInboxHandler : IInboxHandler
{
    private readonly SmokeTestSignals signals;

    public SmokeInboxHandler(SmokeTestSignals signals)
    {
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
    }

    public string Topic => SmokeTopics.Inbox;

    public Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SmokePayload>(message.Payload);
        if (payload != null)
        {
            signals.Signal(payload.RunId, payload.Step, "Processed inbox message.");
        }

        return Task.CompletedTask;
    }
}
