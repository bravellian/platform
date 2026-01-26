# Test Documentation Schema

## Purpose
Provide a consistent XML documentation schema for MSTest, xUnit, and NUnit test methods so docs can be generated deterministically and reviewed quickly.

## Required tags
Use all required tags on every MSTest, xUnit, or NUnit test method. Values should be short and factual.

- `<summary>`: One-line rule statement (If/When/Given ..., then ...).
- `<intent>`: Why the test exists.
- `<scenario>`: Key setup/inputs that make the case interesting.
- `<behavior>`: Observable outcome asserted by the test.

Example (required tags only):

```xml
/// <summary>If the cache is empty, then the provider returns null.</summary>
/// <intent>Document expected behavior for cache lookups.</intent>
/// <scenario>Given an empty cache and a missing key.</scenario>
/// <behavior>Then the lookup result is null.</behavior>
```

## Optional tags
Use these only when the information is already present in the test name/body/inline comments.

- `<failuresignal>`: What a failure likely means or where to look.
- `<origin>`: Reference a defect or requirement with attributes.
  - Attributes: `kind`, `id`, `date`
  - Example: `<origin kind="bug" id="PAY-1842" date="2025-09-03">Fix regression in fee rounding.</origin>`
- `<risk>`: Impact area (money, security, compliance, data-loss).
- `<notes>`: Non-obvious assumptions or environmental constraints.
- `<tags>`: Semicolon-separated list, e.g. `regression; money`.
- `<category>`: Stable grouping for docs. Use a short, human-readable value.
- `<testid>`: Deterministic identifier. Use only if you need to override the default.

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
/// <origin kind="bug" id="SEC-194" date="2025-09-03">Disabled users could sign in.</origin>
/// <testid>Bravellian.Platform.Tests.Auth.SignInTests.Disabled_user_denied</testid>
```

## Formatting rules
- XML doc comments must appear immediately above the method, above any attributes.
- Tags are case-sensitive and must match the schema names exactly.
- Values are trimmed and whitespace runs are collapsed to a single space, except for `<notes>` where whitespace is preserved.
- `<tags>` values are lowercased, de-duped, and sorted.
- Omit optional tags rather than guessing.

## Analyzer guidance
If you use `Bravellian.TestDocs.Analyzers`, it will warn when required tags are missing (`TD001`) or still contain placeholders/empty values (`TD002`). The default template uses `TODO` placeholders that must be replaced.

## Deterministic identifiers
If `<testid>` is omitted, the generator uses:

```
{AssemblyName}:{Namespace}.{Class}.{Method}
```

## Category derivation
If `<category>` is omitted, the generator derives it from namespace:

- If the namespace contains `.Tests.`, the segment after `.Tests.` is used.
- Otherwise the first 2-3 namespace segments are used.

## Validation behavior
- Tests missing any required tag are marked `missing-required`.
- Tests with invalid metadata (for example, `<origin>` without a `kind`) are marked `invalid-format`.
- Missing/invalid metadata does not fail the build by default. `--strict` can enforce failures.

## How generation works
The generator scans C# source for MSTest, xUnit, and NUnit methods (for example `[TestMethod]`, `[Fact]`, `[Theory]`, `[Test]`, `[TestCase]`), parses XML doc comments, normalizes metadata, and produces:

- Markdown docs under `docs/testing/generated/`.
- A machine-readable `stats.json` report.
- A human-readable `stats.md` summary.

See `tools/testdocs/README.md` for running the generator locally.

## stats.json schema

```json
{
  "generatedAtUtc": "2026-01-25T18:00:00Z",
  "repo": { "defaultBranch": "main" },
  "summary": { "total": 0, "compliant": 0, "missingRequired": 0, "invalidFormat": 0 },
  "byCategory": [ { "category": "X", "total": 0, "compliant": 0 } ],
  "byTag": [ { "tag": "regression", "total": 0 } ],
  "byProject": [ { "project": "My.Tests", "total": 0, "compliant": 0 } ],
  "tests": [
    {
      "testId": "...",
      "category": "...",
      "tags": ["..."],
      "origin": { "kind": "bug", "id": "ABC-123", "date": "2025-09-03", "text": "..." },
      "summary": "...",
      "intent": "...",
      "scenario": "...",
      "behavior": "...",
      "failureSignal": "...",
      "risk": "...",
      "notes": "...",
      "source": { "file": "path", "line": 123, "member": "Namespace.Class.Method" },
      "status": "compliant | missing-required | invalid-format"
    }
  ]
}
```
