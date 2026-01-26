# Test Documentation

This folder describes the XML doc schema for MSTest, xUnit, and NUnit tests and hosts generated documentation.

- Schema: `docs/testing/test-doc-schema.md`
- Generated docs: `docs/testing/generated/README.md`

## How to document new tests
1. Add XML doc tags to each MSTest method (`summary`, `intent`, `scenario`, `behavior`).
2. Use optional tags only when they are already supported by the test name/body.
3. Run the generator locally to refresh `docs/testing/generated/`.

Optional: install the analyzer package `Bravellian.TestDocs.Analyzers` to get warnings for missing/placeholder metadata.

Local generation:

```powershell
pwsh ./tools/testdocs/scripts/Invoke-TestDocs.ps1
```
