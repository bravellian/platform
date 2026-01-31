using System.Text.Json.Serialization;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokeFanoutJobPayload(
    [property: JsonPropertyName("fanoutTopic")] string FanoutTopic,
    [property: JsonPropertyName("workKey")] string? WorkKey);
