namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokeStatusSnapshot(
    string Provider,
    bool IsRunning,
    SmokeRunSnapshot? ActiveRun,
    SmokeRunSnapshot? LastRun);
