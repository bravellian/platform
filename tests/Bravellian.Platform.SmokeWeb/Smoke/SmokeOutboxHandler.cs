namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeOutboxHandler : SmokeOutboxSignalHandlerBase
{
    public SmokeOutboxHandler(SmokeTestSignals signals)
        : base(signals)
    {
    }

    public override string Topic => SmokeTopics.Outbox;
}
