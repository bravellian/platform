namespace Bravellian.Platform.SchemaMigrations.Cli;

internal enum SchemaProvider
{
    SqlServer,
    Postgres,
}

internal sealed class SchemaMigrationOptions
{
    public SchemaProvider Provider { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string SchemaName { get; set; } = "infra";

    public bool IncludeControlPlane { get; set; }

    public bool ShowHelp { get; set; }
}
