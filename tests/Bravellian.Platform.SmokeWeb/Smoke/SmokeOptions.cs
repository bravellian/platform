namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeOptions
{
    public string Provider { get; set; } = SmokeProvider.InMemory;

    public string SchemaName { get; set; } = "infra";

    public bool EnableSchemaDeployment { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public string? SqlServerConnectionString { get; set; }

    public string? PostgresConnectionString { get; set; }
}
