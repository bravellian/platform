namespace Bravellian.Platform.SmokeWeb.Smoke;

internal static class SmokeFanoutDefaults
{
    public const string FanoutTopic = "smoke.fanout";
    public const string WorkKey = "default";
    public const string WorkKeyBurst = "burst";
    public const string ShardKey = "default";
    public const string JobTopic = "fanout.coordinate";
    public const string Cron = "*/10 * * * * *";

    public static IReadOnlyList<string> BurstShardKeys { get; } = new[]
    {
        "shard-1",
        "shard-2",
        "shard-3",
        "shard-4",
        "shard-5",
    };

    public static string CoordinatorKey(string workKey) => $"{FanoutTopic}:{workKey}";

    public static string SliceTopic(string workKey) => $"fanout:{FanoutTopic}:{workKey}";

    public static string JobName(string workKey) => $"fanout-{FanoutTopic}-{workKey}";

    public static IReadOnlyList<string> GetShardKeys(string workKey)
    {
        return string.Equals(workKey, WorkKeyBurst, StringComparison.OrdinalIgnoreCase)
            ? BurstShardKeys
            : new[] { ShardKey };
    }

    public static string? ResolveStepName(string workKey)
    {
        if (string.Equals(workKey, WorkKeyBurst, StringComparison.OrdinalIgnoreCase))
        {
            return SmokeStepNames.FanoutBurst;
        }

        if (string.Equals(workKey, WorkKey, StringComparison.OrdinalIgnoreCase))
        {
            return SmokeStepNames.FanoutSmall;
        }

        return null;
    }
}
