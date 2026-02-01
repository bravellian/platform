# Bravellian Platform Schema Migrations CLI

Run the latest Bravellian Platform schema migrations against SQL Server or Postgres.

## Usage

```
bravellian-schema --provider sqlserver --connection-string "<connection string>" --schema infra
bravellian-schema --provider postgres --connection-string "<connection string>" --schema infra
```

Optional flags:
- `--include-control-plane`: also apply the control-plane bundle to the same database.
- `--schema`: override the default schema name (`infra`).

## Examples

```
bravellian-schema -p sqlserver -c "Server=.;Database=Bravellian;Trusted_Connection=True;" -s infra
bravellian-schema -p postgres -c "Host=localhost;Database=bravellian;Username=postgres;Password=postgres" --include-control-plane
```
