# Bravellian.Platform.Email.AspNetCore

ASP.NET Core helpers for Bravellian.Platform.Email outbox integrations.

## Registrations

- `AddBravellianEmailCore` registers the core outbox and processor components.
- `AddBravellianEmailPostmark` registers the Postmark sender adapter.
- `AddBravellianEmailProcessingHostedService` runs the processor on an interval.

See `/docs/email/README.md` for architecture and quick start examples.
