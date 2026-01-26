# TestDocs CLI

This tool extracts XML doc metadata from MSTest, xUnit, and NUnit methods and generates test documentation under `docs/testing/generated/`.

## Requirements
- .NET SDK pinned in `global.json`
- PowerShell 7+ (for scripts)

## Run locally

```powershell
pwsh ./tools/testdocs/scripts/Invoke-TestDocs.ps1
```

Strict mode and compliance threshold:

```powershell
pwsh ./tools/testdocs/scripts/Invoke-TestDocs.ps1 -Strict -MinCompliance 0.9
```

## CLI usage

```powershell
dotnet run --project tools/testdocs/src/TestDocs.Cli -- generate
```

## Dotnet tool usage

```powershell
dotnet tool install --global Bravellian.TestDocs.Cli
bravellian-testdocs generate
```

Options:
- `--repoRoot <path>`
- `--outDir <path>`
- `--strict`
- `--minCompliance <0-1>`
- `--format markdown|json|both`

## Packing for NuGet

```powershell
dotnet pack ./tools/testdocs/TestDocs.sln -c Release -o ./nupkgs
```

## Adding metadata to tests
Follow the schema in `docs/testing/test-doc-schema.md`. Required tags are `summary`, `intent`, `scenario`, and `behavior`.

## Analyzer package
The analyzer package (`Bravellian.TestDocs.Analyzers`) emits warnings for:
- `TD001`: Missing required XML doc tags.
- `TD002`: Placeholder or empty values in required tags.

The default code fix inserts a template with `TODO` placeholders. Replace them with real content to avoid `TD002` warnings.
