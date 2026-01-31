var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql");
var sqlDb = sql.AddDatabase("sqlplatform");

var postgres = builder.AddPostgres("postgres");
var pgDb = postgres.AddDatabase("pgplatform");

builder.AddProject<Projects.Bravellian_Platform_SmokeWeb>("smoke-web")
    .WithReference(sqlDb)
    .WithReference(pgDb)
    .WithEnvironment("Smoke__Provider", "SqlServer");

builder.Build().Run();
