# Operational Troubleshooting Guide

This guide summarizes hands-on procedures for diagnosing and unblocking common operational issues in Bravellian Platform deployments. SQL snippets assume SQL Server and the default schemas/table names; adjust `schemaName`/`tableName` as needed for your environment.

## Stuck Locked Rows

Symptoms: workers stop making progress because rows remain locked or leased to an owner token that is no longer alive.

### Inspect

```sql
-- Show currently locked outbox rows
SELECT Id, Status, LockedUntil, OwnerToken, RetryCount, LastError
FROM [messaging].[Outbox]
WHERE Status = 1 -- InProgress
  AND LockedUntil > SYSUTCDATETIME()
ORDER BY LockedUntil DESC;

-- Inspect inbox rows that never released
SELECT MessageId, Status, LockedUntil, OwnerToken, Attempts, LastError
FROM [messaging].[Inbox]
WHERE Status = 'Processing'
  AND LockedUntil > SYSUTCDATETIME()
ORDER BY LockedUntil DESC;
```

### Recover

1. Confirm the owner process is dead (e.g., instance shutdown or container replaced).
2. Release locks so another worker can claim the rows:
   ```sql
   -- Force-abandon stuck outbox rows
   EXEC [messaging].[Outbox_Abandon] @OwnerToken = '00000000-0000-0000-0000-000000000000';

   -- Reap expired inbox leases and push rows back to Seen
   EXEC [messaging].[Inbox_ReapExpired] @BatchSize = 500;
   ```
3. If you need to accelerate recovery for a specific set of rows, drop the lock and reset status explicitly:
   ```sql
   UPDATE o
   SET Status = 0, LockedUntil = NULL, OwnerToken = NULL
   FROM [messaging].[Outbox] o
   WHERE o.Id IN ('...specific ids...');
   ```
4. Watch application logs/metrics to confirm workers resume progress and retries do not spike.

## High Retry Rates

Symptoms: dispatchers continuously retry the same messages or claim/acknowledge loops appear in metrics.

### Inspect

```sql
-- Outbox messages with the highest retry counts
SELECT TOP (50) Id, Topic, RetryCount, LastError, LockedUntil, Status
FROM [messaging].[Outbox]
WHERE RetryCount > 3
ORDER BY RetryCount DESC, CreatedAt;

-- Inbox messages that flapped between Processing/Seen repeatedly
SELECT TOP (50) MessageId, Attempts, LastError, Status, LastSeenUtc, ProcessedUtc
FROM [messaging].[Inbox]
WHERE Attempts > 5
ORDER BY Attempts DESC, LastSeenUtc DESC;
```

### Recover

1. Inspect `LastError` to identify downstream dependency failures or validation issues.
2. Pause the problematic consumer (if possible) and deploy a fix.
3. For poison messages that cannot succeed, mark them as terminal to stop retries:
   ```sql
   UPDATE [messaging].[Outbox]
   SET Status = 3, LastError = CONCAT('Forced fail at ', SYSUTCDATETIME(), ': ', LastError)
   WHERE Id IN ('...problem ids...');

   UPDATE [messaging].[Inbox]
   SET Status = 'Dead', LastError = CONCAT('Forced dead-letter at ', SYSUTCDATETIME(), ': ', LastError)
   WHERE MessageId IN ('...problem ids...');
   ```
4. Once the fix is live, reset safe messages to `Ready`/`Seen` so they retry cleanly:
   ```sql
   UPDATE [messaging].[Outbox]
   SET Status = 0, LockedUntil = NULL, OwnerToken = NULL, LastError = NULL
   WHERE Id IN ('...safe ids...');

   UPDATE [messaging].[Inbox]
   SET Status = 'Seen', LockedUntil = NULL, OwnerToken = NULL, LastError = NULL
   WHERE MessageId IN ('...safe ids...');
   ```

## Schema Drift

Symptoms: deployments fail on startup, stored procedures are missing, or the platform rejects messages because expected columns are absent.

### Inspect

```sql
-- Confirm required procedures exist for outbox work queue
SELECT name
FROM sys.procedures
WHERE schema_id = SCHEMA_ID('messaging') AND name LIKE 'Outbox_%'
ORDER BY name;

-- Verify inbox table has the expected columns
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'messaging' AND TABLE_NAME = 'Inbox'
ORDER BY ORDINAL_POSITION;
```

For a full manifest comparison, run the repository helper locally:
```bash
./scripts/schema-diff.sh
```
This regenerates the schema hashes and shows differences from `schema-versions.json`.

### Recover

1. If objects are missing, rerun `DatabaseSchemaManager.Ensure*SchemaAsync` for the affected module or temporarily enable `EnableSchemaDeployment = true` on startup to let the service self-heal.
2. For manual environments, apply the current create scripts from the repository (outbox/inbox tables and their stored procedures) before restarting workers.
3. After remediation, rerun `./scripts/schema-diff.sh` to ensure the database matches the expected manifest and capture the upgrade path in change management.

## Fanout/Fan-in “What Happens If” Matrix

| Scenario | Fanout Cursor/Policy Behavior | Fan-in Join Behavior | Recovery Checklist |
| --- | --- | --- | --- |
| Worker crash mid-fanout dispatch | In-flight batch may hold leases until `LockedUntil`; subsequent workers reclaim via `Outbox_Abandon`/`Outbox_ReapExpired`. Cursor remains at last committed offset. | Join membership rows stay pending; no fan-in completion emitted. | Reap expired leases, restart worker, verify `FanoutCursor` caught up and `OutboxJoinMember` rows resume completion. |
| Database failover during fanout | In-progress claims roll back; dispatcher restarts with a fresh fencing token from `OutboxState`. | Join status stays unchanged; partial members remain pending. | Confirm failover completed, reissue claims, and run a short catch-up window while monitoring duplicate deliveries. |
| Schema migration while workers run | New columns/procs may be missing on old nodes; claims can fail with invalid object errors. | Join writes can fail if `OutboxJoin`/`OutboxJoinMember` shape changed. | Deploy migrations first, then restart workers. If errors occurred, replay affected batches after confirming schema matches `schema-versions.json`. |

Keep this matrix visible during onboarding so responders know which parts of the pipeline are safe to retry versus which need schema verification.
