namespace Bravellian.Platform.SmokeWeb.Smoke;

internal static class SmokeFanoutDefaults
{
    public const string FanoutTopic = "smoke.fanout";
    public const string WorkKey = "default";
    public const string ShardKey = "default";
    public const string JobTopic = "fanout.coordinate";
    public const string Cron = "*/10 * * * * *";

    public static string CoordinatorKey => $"{FanoutTopic}:{WorkKey}";

    public static string SliceTopic => $"fanout:{FanoutTopic}:{WorkKey}";

    public static string JobName => $"fanout-{FanoutTopic}-{WorkKey}";
}
