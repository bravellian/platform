var builder = DistributedApplication.CreateBuilder(args);

var provider = builder.Configuration["Smoke:Provider"];

var enableSql = GetBool(builder.Configuration["Smoke:EnableSqlServer"], true);
var enablePostgres = GetBool(builder.Configuration["Smoke:EnablePostgres"], true);

if (!string.IsNullOrWhiteSpace(provider))
{
    enableSql = string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase);
    enablePostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);
}

if (!enableSql && !enablePostgres)
{
    throw new InvalidOperationException("At least one provider must be enabled for the smoke web app.");
}

var sqlDb = enableSql
    ? builder.AddSqlServer("sql").AddDatabase("sqlplatform")
    : null;

var pgDb = enablePostgres
    ? builder.AddPostgres("postgres").AddDatabase("pgplatform")
    : null;

if (string.IsNullOrWhiteSpace(provider))
{
    provider = enableSql ? "SqlServer" : "Postgres";
}

var smoke = builder.AddProject<Projects.Bravellian_Platform_SmokeWeb>("smoke-web")
    .WithEnvironment("Smoke__Provider", provider);

if (sqlDb != null)
{
    smoke.WithReference(sqlDb, "SqlServer")
        .WaitFor(sqlDb);
}

if (pgDb != null)
{
    smoke.WithReference(pgDb, "Postgres")
        .WaitFor(pgDb);
}

await builder.Build().RunAsync();

static bool GetBool(string? value, bool defaultValue)
{
    return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}
