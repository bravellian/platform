namespace Bravellian.Platform.SmokeWeb.Smoke;

internal sealed class SmokeSchedulerOutboxHandler : SmokeOutboxSignalHandlerBase
{
    public SmokeSchedulerOutboxHandler(SmokeTestSignals signals)
        : base(signals)
    {
    }

    public override string Topic => SmokeTopics.Scheduler;
}
