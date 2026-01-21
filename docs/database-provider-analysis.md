# Database Provider Split & Postgres Readiness Notes

Last updated: 2025-02-14

## Goal
Split the platform into a provider-agnostic core (`Bravellian.Platform`) plus a SQL Server provider package (proposed: `Bravellian.Platform.SqlServer`). This isolates SQL Server-only types, stored procedures, and SQL-specific DI wiring so future providers (e.g., Postgres) can be added without entangling the core.

## Current SQL Server Surface Area (high-level)
- SqlClient usage across outbox/inbox/scheduler/fanout/leases/semaphores/metrics.
- Schema deployment and stored procedure generation in `DatabaseSchemaManager` and SQL scripts in `src/Bravellian.Platform.Database/*.sql`.
- DI entrypoints register SQL Server implementations directly (e.g., `AddSqlOutbox`, `AddPlatformMultiDatabaseWithList`).
- Platform lifecycle validation uses `SqlConnection`.
- Dapper type handlers are registered from core, but only used by SQL Server implementations.

## SQL Server-specific features to extract
These are SQL Server-only constructs that should live in the SQL Server provider:
- T-SQL stored procedures, `MERGE`, `OUTPUT inserted`, `TOP`, `@@ROWCOUNT`, `SCOPE_IDENTITY`, `CREATE OR ALTER PROCEDURE`.
- Locking hints: `READPAST`, `UPDLOCK`, `ROWLOCK`, `HOLDLOCK`.
- `sp_getapplock` gate/lease behavior.
- SQL Server types: `UNIQUEIDENTIFIER`, `NVARCHAR(MAX)`, `DATETIMEOFFSET`, `ROWVERSION`, `BIT`, `SYSNAME`, `IDENTITY`.
- Table-valued parameters (`GuidIdList`, `StringIdList`).
- Columnstore index usage for metrics.

## Proposed Project Split
### Core (`Bravellian.Platform`)
Keep provider-agnostic abstractions and orchestration:
- Interfaces: `IOutbox`, `IOutboxStore`, `IInbox`, `IScheduler*`, `IFanout*`, `ISystemLease`, `ISemaphoreService`.
- Routing/selection: `*Router`, `*SelectionStrategy`, round-robin strategies.
- Background orchestration that is not tied to a DB provider (e.g., multi-outbox/inbox polling).
- General utilities and identifiers.
- `IDatabaseSchemaCompletion` + `DatabaseSchemaCompletion` (provider-agnostic coordination).
- Provider-agnostic DI helpers (e.g., `AddTimeAbstractions`, handler registration).

### SQL Server Provider (`Bravellian.Platform.SqlServer`)
Move SQL Server-specific code and API surface here:
- All `Sql*` implementations and options/validators.
- Schema deployment (`DatabaseSchemaManager`, `DatabaseSchemaBackgroundService`) and SQL artifacts.
- SQL Server DI entrypoints (`AddSqlOutbox`, `AddSqlInbox`, `AddSqlScheduler`, `AddPlatform*` that register SQL Server providers).
- Dapper type handler registration/module initializer (SQL Server provider will own Dapper).
- SQL Server lifecycle validation (`PlatformLifecycleService`).

## Postgres Readiness (to revisit later)
When provider split is stable, evaluate Postgres implementation details:
- `MERGE` -> `INSERT ... ON CONFLICT DO UPDATE` with `RETURNING`.
- `READPAST`/`UPDLOCK` -> `FOR UPDATE SKIP LOCKED`.
- `sp_getapplock` -> `pg_try_advisory_lock` / `pg_try_advisory_xact_lock`.
- Table-valued params -> `uuid[]`/`text[]` + `UNNEST` or temp tables.
- Columnstore -> B-tree/BRIN/TimescaleDB alternatives.
- `ROWVERSION` -> `xmin` or explicit `bigint` version column.

## Open Questions / Follow-ups
- Should `PlatformServiceCollectionExtensions` (AddPlatform* helpers) stay in core or move to the SQL Server provider?
- Should SQL artifacts in `src/Bravellian.Platform.Database` move under the SQL Server provider package?
- Do we want core to expose any provider-agnostic schema deployment interface?

## DBUp Migration Analysis (schema management in-library)
### Current state (baseline)
- Schema deployment is done via `DatabaseSchemaManager` and ad-hoc SQL execution with batch splitting.
- Schema integrity is checked in tests via `schema-versions.json` snapshot hashes (see `tests/Bravellian.Platform.Tests/SchemaVersionSnapshot.cs`).
- SQL artifacts live under `src/Bravellian.Platform.Database/*.sql` and are executed in order in tests.

### DBUp feasibility
- DBUp supports SQL Server and Postgres via provider packages (`DbUp.SqlServer`, `DbUp.Postgresql`).
- Embedding scripts as resources or loading from disk is supported; we can keep provider-specific script sets.
- DBUp is “up” only and tracks applied scripts in a journal table; it does not do full drift detection.

### What changes are required
- Replace `DatabaseSchemaManager` with DBUp-based migration runner per provider.
- Move SQL artifacts into provider-specific migration folders:
  - `src/Bravellian.Platform.SqlServer/Migrations/*.sql`
  - `src/Bravellian.Platform.Postgres/Migrations/*.sql` (future)
- Add script naming conventions for ordering (e.g., `001_Initial.sql`, `002_Add_Column.sql`).
- Use provider-specific script batches (SQL Server uses `GO`; Postgres does not).
- Update schema deployment flow (currently `DatabaseSchemaBackgroundService`) to run DBUp per database discovered.
- Replace `schema-versions.json` snapshots with DBUp journal snapshots or keep snapshot mechanism as an additional drift check.

### DBUp limitations / drift detection
- DBUp only knows what scripts have run; it does not detect manual drift in schema objects.
- Drift detection options:
  - Keep `schema-versions.json` hashing to verify expected scripts vs. production.
  - Add lightweight schema verification checks (e.g., required tables/columns) similar to current tests.
  - Use an external schema compare tool in CI if deeper drift detection is needed.

### SQL Server specifics to consider
- `CREATE OR ALTER PROCEDURE` and statements with `GO` require SQL Server-specific batch splitting (DBUp supports this for SQL Server scripts).
- Some statements are not allowed inside a transaction; DBUp supports `RunInTransaction(false)` per script if needed.
- Current stored procedures can remain, but should be moved into ordered migration scripts.

### Postgres considerations (for parity later)
- Need a parallel migration set with `CREATE FUNCTION`/`CREATE PROCEDURE` equivalents.
- Replace SQL Server-only types and locking hints as noted earlier.
- Ensure ordering is consistent so logical schema version numbers align across providers.

### Suggested migration plan
1) Introduce DBUp in SQL Server provider: embed `*.sql` migrations and replace `DatabaseSchemaManager` usage.
2) Keep schema snapshot tests, but drive hashes from DBUp migration list rather than `DatabaseSchemaManager`.
3) Add a provider-agnostic migration runner interface in core to call the provider-specific DBUp runner.
4) Later, add Postgres provider with a parallel migration set and comparable version numbering.

