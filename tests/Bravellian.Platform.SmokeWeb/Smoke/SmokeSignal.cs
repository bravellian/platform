namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed record SmokeSignal(string StepName, bool IsSuccess, string? Message)
{
    public static SmokeSignal Success(string stepName, string? message)
        => new(stepName, true, message);

    public static SmokeSignal Timeout(string stepName)
        => new(stepName, false, "Timed out waiting for handler.");
}
