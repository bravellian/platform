# Test XML Doc Schema

## Purpose
Provide a consistent, minimal XML documentation schema for MSTest test methods so readers can quickly understand intent, setup, and expected behavior.

## Required tags
Use these tags for every test method. Keep each tag value short (about 200 characters or less) and factual.

- `<summary>`: One-line rule statement, ideally in the form "If/When/Given ..., then ...".
- `<intent>`: Why the test exists.
- `<scenario>`: Setup or inputs that make the case interesting.
- `<behavior>`: Observable outcome asserted by the test.

Example (required tags only):

```xml
/// <summary>If the cache is empty, then the provider returns null.</summary>
/// <intent>Document expected behavior for cache lookups.</intent>
/// <scenario>Given an empty cache and a missing key.</scenario>
/// <behavior>Then the lookup result is null.</behavior>
```

## Optional tags
Include only when the information is obvious from the test name/body or existing comments. If unclear, omit.

- `<failuresignal>`: What a failure likely means or where to look.
- `<origin>`: Reference only when present in code comments or names. Do not invent.
  Example: `<origin kind="bug" id="PAY-1842" date="2025-09-03">Fix regression in fee rounding.</origin>`
- `<risk>`: Impact area such as money, security, compliance, or data-loss.
- `<notes>`: Non-obvious assumptions, timing, flakiness history, or environment constraints.
- `<tags>`: Semicolon-separated, 1-3 tags, e.g. `regression; money`.
- `<category>`: Stable grouping, usually derived from namespace or folder.
- `<testid>`: Deterministic identifier, e.g. `{RootNamespace}.{ClassName}.{MethodName}`.

Example (with optional tags):

```xml
/// <summary>When a disabled user signs in, then access is denied.</summary>
/// <intent>Document security expectations for disabled accounts.</intent>
/// <scenario>Given a disabled user and valid credentials.</scenario>
/// <behavior>Then the sign-in attempt is rejected.</behavior>
/// <failuresignal>Auth pipeline may be skipping account state checks.</failuresignal>
/// <risk>security</risk>
/// <tags>security; regression</tags>
/// <category>Auth.SignIn</category>
/// <testid>Bravellian.Platform.Tests.Auth.SignInTests.Disabled_user_denied</testid>
```

## Formatting rules
- Place XML doc comments immediately above the method, above any attributes.
- Do not add information that is not present in the test name/body/comments.
- Do not include `<origin>` unless a reference already exists.
- Keep tag values concise; avoid prose paragraphs.
- Tags are case-sensitive and must match the schema exactly.

## Guidance for contributors
- Keep comments short and factual.
- Prefer omission over guessing for optional tags.
- If a method already has XML docs, only add missing required tags.

## Extending the schema
Add new optional tags sparingly. Update this document with:
- The tag definition
- When to use it
- A short example
