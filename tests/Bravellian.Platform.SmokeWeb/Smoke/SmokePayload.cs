namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokePayload(
    string RunId,
    string Step,
    DateTimeOffset EnqueuedAtUtc);
