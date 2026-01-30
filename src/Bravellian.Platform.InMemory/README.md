# Bravellian.Platform.InMemory

In-memory provider for Bravellian.Platform.

This package provides in-memory implementations of outbox, inbox, scheduler, fanout, and leases for testing
or single-process development scenarios where a database is not desired.

## Notes
- This provider is not durable and does not provide cross-process coordination.
- Leases, timers, and queues live only in memory for the current process.
