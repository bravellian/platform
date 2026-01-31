namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokeStepSnapshot(
    string Name,
    string Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message);
