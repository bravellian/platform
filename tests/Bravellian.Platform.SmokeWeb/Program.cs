using System.Net;
using Bravellian.Platform;
using Bravellian.Platform.SmokeWeb.Smoke;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SmokeOptions>(builder.Configuration.GetSection("Smoke"));

var smokeOptions = builder.Configuration.GetSection("Smoke").Get<SmokeOptions>() ?? new SmokeOptions();
var provider = NormalizeProvider(smokeOptions.Provider);

var sqlConnection = ResolveConnectionString(
    builder.Configuration,
    smokeOptions.SqlServerConnectionString,
    "SqlServer",
    "sqlplatform");

var postgresConnection = ResolveConnectionString(
    builder.Configuration,
    smokeOptions.PostgresConnectionString,
    "Postgres",
    "pgplatform");

switch (provider)
{
    case SmokeProvider.SqlServer:
        if (string.IsNullOrWhiteSpace(sqlConnection))
        {
            throw new InvalidOperationException("SQL Server provider selected but no connection string was provided.");
        }

        builder.Services.AddSqlPlatform(new SqlPlatformOptions
        {
            ConnectionString = sqlConnection,
            SchemaName = smokeOptions.SchemaName,
            EnableSchemaDeployment = smokeOptions.EnableSchemaDeployment,
        });
        break;

    case SmokeProvider.Postgres:
        if (string.IsNullOrWhiteSpace(postgresConnection))
        {
            throw new InvalidOperationException("Postgres provider selected but no connection string was provided.");
        }

        builder.Services.AddPostgresPlatform(new PostgresPlatformOptions
        {
            ConnectionString = postgresConnection,
            SchemaName = smokeOptions.SchemaName,
            EnableSchemaDeployment = smokeOptions.EnableSchemaDeployment,
        });
        break;

    case SmokeProvider.InMemory:
        builder.Services.AddInMemoryPlatformMultiDatabaseWithList(new[]
        {
            new InMemoryPlatformDatabase { Name = "default" },
        });
        break;

    default:
        throw new InvalidOperationException($"Unknown smoke provider '{provider}'.");
}

builder.Services.AddSingleton(new SmokeRuntimeInfo
{
    Provider = provider,
    SchemaName = smokeOptions.SchemaName,
    EnableSchemaDeployment = smokeOptions.EnableSchemaDeployment,
    ConnectionString = provider switch
    {
        SmokeProvider.SqlServer => sqlConnection,
        SmokeProvider.Postgres => postgresConnection,
        _ => null,
    },
    ConnectionStringSource = provider switch
    {
        SmokeProvider.SqlServer => ResolveConnectionStringSource(builder.Configuration, smokeOptions.SqlServerConnectionString, "SqlServer", "sqlplatform"),
        SmokeProvider.Postgres => ResolveConnectionStringSource(builder.Configuration, smokeOptions.PostgresConnectionString, "Postgres", "pgplatform"),
        _ => null,
    },
});

builder.Services.AddSmokeServices();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", (SmokeRuntimeInfo info) =>
    Results.Content(BuildHomePage(info), "text/html"));

app.MapGet("/api/status", (SmokeTestState state, SmokeRuntimeInfo info) =>
    Results.Ok(state.GetStatusSnapshot(info.Provider)));

app.MapPost("/api/run", async (SmokeTestRunner runner, CancellationToken ct) =>
{
    var run = await runner.StartAsync(ct).ConfigureAwait(false);
    return Results.Ok(run.ToSnapshot());
});

app.MapPost("/api/reset", (SmokeTestState state) =>
{
    state.Reset();
    return Results.Ok();
});

app.Run();

static string NormalizeProvider(string provider)
{
    if (string.IsNullOrWhiteSpace(provider))
    {
        return SmokeProvider.InMemory;
    }

    if (string.Equals(provider, SmokeProvider.SqlServer, StringComparison.OrdinalIgnoreCase))
    {
        return SmokeProvider.SqlServer;
    }

    if (string.Equals(provider, SmokeProvider.Postgres, StringComparison.OrdinalIgnoreCase))
    {
        return SmokeProvider.Postgres;
    }

    if (string.Equals(provider, SmokeProvider.InMemory, StringComparison.OrdinalIgnoreCase))
    {
        return SmokeProvider.InMemory;
    }

    return provider;
}

static string ResolveConnectionString(
    IConfiguration configuration,
    string? directValue,
    string primaryKey,
    string aspireKey)
{
    if (!string.IsNullOrWhiteSpace(directValue))
    {
        return directValue;
    }

    return configuration.GetConnectionString(primaryKey)
        ?? configuration.GetConnectionString(aspireKey)
        ?? string.Empty;
}

static string? ResolveConnectionStringSource(
    IConfiguration configuration,
    string? directValue,
    string primaryKey,
    string aspireKey)
{
    if (!string.IsNullOrWhiteSpace(directValue))
    {
        return $"Smoke:{primaryKey}ConnectionString";
    }

    if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(primaryKey)))
    {
        return $"ConnectionStrings:{primaryKey}";
    }

    if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(aspireKey)))
    {
        return $"ConnectionStrings:{aspireKey}";
    }

    return null;
}

static string BuildHomePage(SmokeRuntimeInfo info)
{
    var provider = WebUtility.HtmlEncode(info.Provider);
    var schema = WebUtility.HtmlEncode(info.SchemaName);
    var connection = WebUtility.HtmlEncode(info.MaskedConnectionString ?? string.Empty);
    var source = WebUtility.HtmlEncode(info.ConnectionStringSource ?? "n/a");
    var schemaDeploy = info.EnableSchemaDeployment ? "enabled" : "disabled";

    var template = @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Bravellian Platform Smoke</title>
  <style>
    :root {
      --bg: #0f172a;
      --panel: #111827;
      --text: #f8fafc;
      --muted: #94a3b8;
      --accent: #38bdf8;
      --success: #22c55e;
      --fail: #f87171;
      --running: #facc15;
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: ""Segoe UI"", ""Helvetica Neue"", Arial, sans-serif;
      background: radial-gradient(circle at top left, #1e293b, #0f172a 40%);
      color: var(--text);
      min-height: 100vh;
      padding: 32px;
    }

    header {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-bottom: 24px;
    }

    h1 {
      margin: 0;
      font-size: 28px;
      letter-spacing: 0.02em;
    }

    .meta {
      color: var(--muted);
      font-size: 14px;
    }

    .grid {
      display: grid;
      gap: 16px;
      grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
      margin-bottom: 24px;
    }

    .card {
      background: rgba(17, 24, 39, 0.8);
      border: 1px solid rgba(148, 163, 184, 0.15);
      border-radius: 12px;
      padding: 16px;
    }

    .card h2 {
      font-size: 14px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      margin: 0 0 12px;
      color: var(--muted);
    }

    .actions {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
    }

    button {
      border: none;
      border-radius: 8px;
      padding: 10px 16px;
      font-size: 14px;
      cursor: pointer;
      background: var(--accent);
      color: #0f172a;
      font-weight: 600;
    }

    button.secondary {
      background: transparent;
      color: var(--text);
      border: 1px solid rgba(148, 163, 184, 0.4);
    }

    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 12px;
      font-size: 14px;
    }

    th, td {
      text-align: left;
      padding: 8px 0;
      border-bottom: 1px solid rgba(148, 163, 184, 0.12);
    }

    .status {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-weight: 600;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--muted);
    }

    .status.running .dot { background: var(--running); }
    .status.succeeded .dot { background: var(--success); }
    .status.failed .dot { background: var(--fail); }

    .muted { color: var(--muted); }

    pre {
      background: rgba(15, 23, 42, 0.8);
      padding: 12px;
      border-radius: 8px;
      font-size: 12px;
      overflow-x: auto;
    }
  </style>
</head>
<body>
  <header>
    <h1>Bravellian Platform Smoke Lab</h1>
    <div class=""meta"">Provider: <strong>__PROVIDER__</strong> · Schema: <strong>__SCHEMA__</strong> · Schema deploy: <strong>__SCHEMA_DEPLOY__</strong></div>
    <div class=""meta"">Connection: <strong>__CONNECTION__</strong> (<span>__SOURCE__</span>)</div>
  </header>

  <section class=""grid"">
    <div class=""card"">
      <h2>Actions</h2>
      <div class=""actions"">
        <button id=""run"">Run Smoke Test</button>
        <button id=""reset"" class=""secondary"">Reset</button>
      </div>
      <p class=""muted"">Switch providers by setting <code>Smoke:Provider</code> in config and restarting the app.</p>
    </div>
    <div class=""card"">
      <h2>Status</h2>
      <div id=""status"">Loading…</div>
    </div>
  </section>

  <section class=""card"">
    <h2>Latest Run</h2>
    <div id=""run-details"">No runs yet.</div>
  </section>

<script>
const statusEl = document.getElementById('status');
const runDetailsEl = document.getElementById('run-details');
const runButton = document.getElementById('run');
const resetButton = document.getElementById('reset');

runButton.addEventListener('click', async () => {
  runButton.disabled = true;
  await fetch('/api/run', { method: 'POST' });
  runButton.disabled = false;
  await refresh();
});

resetButton.addEventListener('click', async () => {
  await fetch('/api/reset', { method: 'POST' });
  await refresh();
});

function statusBadge(step) {
  const status = (step?.status || 'Pending').toLowerCase();
  return `<span class=""status ${status}""><span class=""dot""></span>${status}</span>`;
}

function renderRun(run) {
  if (!run) {
    runDetailsEl.innerHTML = '<span class=""muted"">No run recorded.</span>';
    return;
  }

  const steps = run.steps || [];
  const rows = steps.map(step => {
    const started = step.startedAtUtc ? new Date(step.startedAtUtc).toLocaleTimeString() : '-';
    const completed = step.completedAtUtc ? new Date(step.completedAtUtc).toLocaleTimeString() : '-';
    const message = step.message || '';
    return `
      <tr>
        <td>${step.name}</td>
        <td>${statusBadge(step)}</td>
        <td>${started}</td>
        <td>${completed}</td>
        <td>${message}</td>
      </tr>`;
  }).join('');

  runDetailsEl.innerHTML = `
    <div class=""muted"">Run ID: ${run.runId}</div>
    <table>
      <thead>
        <tr>
          <th>Step</th>
          <th>Status</th>
          <th>Started</th>
          <th>Completed</th>
          <th>Message</th>
        </tr>
      </thead>
      <tbody>${rows}</tbody>
    </table>`;
}

async function refresh() {
  const response = await fetch('/api/status');
  const data = await response.json();

  statusEl.textContent = data.isRunning
    ? 'Smoke run in progress…'
    : 'Idle. Ready to run.';

  renderRun(data.activeRun || data.lastRun);
}

refresh();
setInterval(refresh, 1500);
</script>
</body>
</html>
";

    return template
        .Replace("__PROVIDER__", provider, StringComparison.Ordinal)
        .Replace("__SCHEMA__", schema, StringComparison.Ordinal)
        .Replace("__SCHEMA_DEPLOY__", schemaDeploy, StringComparison.Ordinal)
        .Replace("__CONNECTION__", connection, StringComparison.Ordinal)
        .Replace("__SOURCE__", source, StringComparison.Ordinal);
}
