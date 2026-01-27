# Observability Guide

This guide describes when to use audit events, operation tracking, and platform observability conventions,
and how to apply them in apps that consume Bravellian.Platform.

## Goals

- Provide a consistent, queryable timeline of what happened.
- Support correlation across inbox, outbox, webhooks, and background jobs.
- Keep instrumentation lightweight and optional for app teams.

## Which primitive to use

- **Audit events** (`Bravellian.Platform.Audit`)
  - Use for immutable, human-readable timeline entries.
  - Best for compliance, user-facing history, and support investigations.
  - Event names should be lowercase and dot-separated (for example, `invoice.created`).

- **Operations** (`Bravellian.Platform.Operations`)
  - Use for long-running or multi-step work that needs progress and status.
  - Best for jobs, batch work, and webhook processing runs.
  - Operation names should be stable and dot-separated (for example, `Webhook.Process`).

- **Observability conventions** (`Bravellian.Platform.Observability`)
  - Use to keep names and tags consistent across subsystems.
  - Prefer `PlatformEventNames` and `PlatformTagKeys` for standard signals.

## When to emit

- **Boundary entry points**
  - HTTP endpoints, webhook handlers, message consumers.
  - Start operations and emit audit events for meaningful transitions.

- **Background processing loops**
  - Outbox processing, scheduler runs, or maintenance tasks.
  - Record completion and failures with consistent tags.

- **External side effects**
  - Email sends, webhook callbacks, and integrations.
  - Emit audit events for attempted, failed, and completed outcomes.

## Naming and tags

- Audit event names: lowercase, dot-separated (`email.sent`, `outbox.message.processed`).
- Operation names: stable and dot-separated (`Webhook.Process`, `Email.Send`).
- Tags: use `PlatformTagKeys` for shared identifiers such as `tenant`, `provider`, `messageKey`, and `operationId`.

## Correlation

- Prefer flowing `CorrelationContext` to connect audit and operation records.
- If a correlation context is not available, emit events without correlation rather than inventing IDs.

## PII and payload guidance

- Do not store raw request bodies or secrets in `DisplayMessage` or `DataJson`.
- Prefer stable IDs and minimal metadata; keep payloads small.

## Optional integration patterns

These are recommended but not required:

- **Middleware/filters**: emit a standard audit event for inbound HTTP requests.
- **DI decorators**: wrap webhook handlers or processors to start operations automatically.
- **Hosted services**: emit completion events for periodic tasks.

## Analyzer support

The observability analyzer package (`Bravellian.Platform.Observability.Analyzers`) ships with
conventions such as:

- `OBS001`: Audit event names should be lowercase and dot-separated.

Use it as a development dependency to keep instrumentation consistent without runtime coupling.
