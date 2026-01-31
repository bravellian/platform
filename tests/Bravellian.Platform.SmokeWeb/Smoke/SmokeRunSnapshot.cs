namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokeRunSnapshot(
    string RunId,
    string Provider,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<SmokeStepSnapshot> Steps);
