# Platform QoL & Production Readiness Notes

Captured from planning discussion on 2026-02-03.

## Pain points (current)
- Most painful: diagnosing why something went wrong, especially when root cause is inside library.
- Recent example: webhook handler failures. Only 12 retries, no easy replay path; required building a separate manual recovery flow.

## Candidate improvements (keep on list)
### High priority
- Operational visibility: dashboard or shared view of inbox/outbox/scheduler/queues, leases, stuck items, retry spikes, and "time since last success".
- Dead-letter/retry policy: ability to identify failed items and replay or fix in place.
- Introspection/traceability: correlate webhook ingress -> inbox -> scheduler -> outbox -> delivery, beyond raw logging.
- Consistency & guardrails: policy bundles for retries/backoff/jitter, max lease time, idempotency rules, thresholds.

### Medium / later
- Chaos/failure simulation: explore later; unclear how to do without overhead.
- CLI tooling: useful but can come later.
- Tuning hints: nice-to-have, lower priority.

## Open questions
- Should operational dashboard be internal-only tooling, or exposed to every org/tenant as a product feature?
- What is the desired DLQ semantics for webhooks and other primitives (replay, mutate, route)?

## Scope principle
- Keep a backlog of all ideas (including later items) for future feature/requirement requests.
