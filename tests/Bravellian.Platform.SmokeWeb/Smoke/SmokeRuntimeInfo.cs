namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeRuntimeInfo
{
    public required string Provider { get; init; }

    public required string SchemaName { get; init; }

    public required bool EnableSchemaDeployment { get; init; }

    public string? ConnectionString { get; init; }

    public string? ConnectionStringSource { get; init; }

    public string? MaskedConnectionString => ConnectionString is null
        ? null
        : MaskConnectionString(ConnectionString);

    private static string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        var visibleChars = Math.Min(6, connectionString.Length);
        return string.Concat(connectionString.AsSpan(0, visibleChars), "…");
    }
}
