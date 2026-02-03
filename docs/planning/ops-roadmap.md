# Internal Ops Roadmap (Draft)

Captured: 2026-02-03

## 1) Inbox/Work Queue Recovery (in progress)
**Goal**: Make it easy to recover from dead/poisoned work items without writing ad-hoc recovery code.

**MVP scope**
- Add a `ReviveAsync` operation on inbox work stores to requeue Dead items.
- Preserve last error by default; allow optional reason and delay.
- Ensure in-memory, SQL Server, and Postgres implementations behave consistently.

**Success metrics**
- Operator can requeue a failed webhook message and observe it processed again.
- One-step recovery (no custom code) for dead inbox items.

## 2) Internal Ops Dashboard (next)
**Goal**: A single internal view for queue health and stuck items across inbox/outbox/scheduler.

**MVP scope**
- Read-only list views: pending/processing/failed counts, oldest age, last success time.
- Drill into dead items (message id, topic, last error, attempts, due time).
- Requeue action wired to `ReviveAsync` (inbox) and equivalent outbox actions.

**Success metrics**
- Triage time for "why did it fail" reduced to < 5 minutes.
- At least one production incident resolved via dashboard only.

## 3) Traceability/Lineage (planned)
**Goal**: End-to-end linking from webhook ingress → inbox → scheduler → outbox → delivery.

**MVP scope**
- Standard correlation fields on inbox and emitted audit events.
- Consistent tags on platform events (message id, topic, correlation id, provider event id).
- Simple query API for lineage by correlation id.

**Success metrics**
- Can trace a single webhook event through all stages without logs.

## 4) Policy Bundles & Guardrails (planned)
**Goal**: Default policies for retries/backoff/jitter, lease timeouts, and max attempts.

**MVP scope**
- Package defaults and validation errors for unsafe configs.
- Shared config model across inbox/outbox/scheduler.

**Success metrics**
- New services have sane retry/lease behavior without custom tuning.
