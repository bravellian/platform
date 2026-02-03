# Schema Alignment Plan (Inbox/Outbox)

Captured: 2026-02-03

## Goals
- Standardize column naming and semantics across inbox and outbox.
- Make changes migration-first for seamless deployment.
- Ensure code works with both old and new schema for 1–2 versions.
- Prefer date/timestamp column names ending with "On" (e.g., CreatedOn, ProcessedOn).

## Current Inconsistencies (Inventory)
### Identifiers
- Inbox primary key: MessageId (string)
- Outbox primary key: Id (uuid) with separate MessageId (uuid)

### Timestamps
- Inbox: FirstSeenUtc, LastSeenUtc, ProcessedUtc
- Outbox: CreatedAt, ProcessedAt

### Retry Counters
- Inbox: Attempts
- Outbox: RetryCount

### Status
- Inbox: Status (string) with Seen/Processing/Done/Dead
- Outbox: Status (smallint) with Ready/InProgress/Done/Failed and IsProcessed (bool)

### Other Fields
- Inbox-only: Source, Hash
- Outbox-only: ProcessedBy, CorrelationId

### Cleanup Semantics
- Inbox cleanup deletes Done + ProcessedUtc < cutoff
- Outbox cleanup deletes IsProcessed = 1 + ProcessedAt < cutoff

## Proposed Canonical Naming (Target Schema)
### Shared work-queue columns
- WorkItemId (string/uuid): keep type per table, but standardize name
- Topic
- Payload
- Status
- AttemptCount
- LastError
- LockedUntil
- OwnerToken
- DueOn

### Timestamps (use "On" suffix)
- CreatedOn
- FirstSeenOn (inbox-specific)
- LastSeenOn (inbox-specific)
- ProcessedOn

### Correlation/Audit
- CorrelationId (optional, add to inbox)
- ProcessedBy (optional, add to inbox if useful)

### Inbox-specific (still ok)
- Source
- Hash

## Phased Migration Strategy
### Phase 0: Mapping layer (code-first, no schema changes)
- Add a single internal mapping model that can read/write both old and new column names.
- Prefer new names when present; fall back to old names.
- When writing, write both (if new columns exist) so either schema works.

### Phase 1: Additive migrations
- Add new canonical columns to both Inbox and Outbox tables.
- Backfill new columns from old columns when possible.
- Create computed or redundant columns only if cheap; otherwise keep both for 1–2 versions.

### Phase 2: Dual-write + dual-read (1–2 releases)
- Update data access to read new columns if present; otherwise fallback.
- Update writes to populate both old and new columns.
- Update indexes to include new canonical columns where appropriate.

### Phase 3: Flip default + deprecate
- Switch reads to new columns; keep fallback for one release.
- Update procedures/queries to use canonical columns.
- Mark old columns as deprecated in docs.

### Phase 4: Cleanup
- Remove old columns in a later migration once safe.
- Drop legacy indexes and update cleanup procedures.

## Proposed Column Mapping (Old -> New)
### Inbox
- MessageId -> WorkItemId (or keep MessageId, but introduce WorkItemId alias)
- FirstSeenUtc -> FirstSeenOn
- LastSeenUtc -> LastSeenOn
- ProcessedUtc -> ProcessedOn
- Attempts -> AttemptCount
- DueTimeUtc -> DueOn

### Outbox
- Id -> WorkItemId (or keep Id, but introduce WorkItemId alias)
- CreatedAt -> CreatedOn
- ProcessedAt -> ProcessedOn
- RetryCount -> AttemptCount
- DueTimeUtc -> DueOn

## Status Standardization (Concrete Values)
### Canonical status set (used by both Inbox and Outbox)
- Ready
- Processing
- Done
- Dead

### Mapping from existing Inbox values
- Seen -> Ready
- Processing -> Processing
- Done -> Done
- Dead -> Dead

### Mapping from existing Outbox values
- Status 0 (Ready) -> Ready
- Status 1 (InProgress) -> Processing
- Status 2 (Done) -> Done
- Status 3 (Failed) -> Dead

### Notes
- “Dead” is the unified terminal failure state (replaces “Failed”).
- Use status only; deprecate IsProcessed once migrations are complete.

## Phase 1 Migration Checklist (Additive, Non-breaking)
### SQL Server
1) Add new canonical columns to inbox/outbox (nullable where needed):
   - Inbox: WorkItemId (alias if keeping MessageId), CreatedOn, ProcessedOn, AttemptCount, DueOn, CorrelationId, ProcessedBy, StatusCode (optional if migrating to numeric).
   - Outbox: WorkItemId (alias if keeping Id), CreatedOn, ProcessedOn, AttemptCount, DueOn.
2) Backfill new columns from old columns (one-time update scripts).
3) Add new indexes aligned to canonical columns (e.g., Status + CreatedOn/LastSeenOn + DueOn).
4) Keep existing columns and indexes for 1–2 releases.

### Postgres
1) Add new canonical columns to inbox/outbox (same set as above).
2) Backfill new columns from old columns (single UPDATE or batched approach).
3) Add new indexes aligned to canonical columns.
4) Keep existing columns and indexes for 1–2 releases.

### App code (same release as Phase 1)
- Dual-read: prefer new columns when present; fallback to old columns.
- Dual-write: write to both old and new columns when new columns exist.
- Avoid relying on IsProcessed once status is unified.

## Open Decisions (Resolved)
- Primary key: standardize on WorkItemId, but allow “InboxMessageId” / “OutboxMessageId” naming in APIs if desired.
- Status representation: unify on a single status vocabulary across inbox/outbox (Ready/Processing/Done/Dead).
- IsProcessed: deprecate/remove once status is unified.
- Add CorrelationId to inbox: yes.
- Add ProcessedBy to inbox: yes.

## Risks
- Backward-compatibility across multi-tenant/multi-database deployments.
- Online migrations for large tables (backfill must be incremental).
- Index changes may require maintenance windows depending on DB size.

## Next Step (Recommended)
1) Decide on primary key naming convention (WorkItemId vs keep Id/MessageId). 
2) Decide on Status representation (string vs numeric). 
3) Implement Phase 1 additive migrations for both SQL Server and Postgres.

