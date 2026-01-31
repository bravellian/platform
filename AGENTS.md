# Repository Guidelines

## Early Repo Orientation (run first)
We do not have LSP, so run these skills first to build a semantic picture and avoid guessing.
- dotnet-repo-discovery: Generate the solution map. Run: `pwsh -File .codex/skills/dotnet-repo-discovery/scripts/discover-dotnet-repo.ps1`
- dotnet-build-diagnostics: Capture build diagnostics with a binlog and summary. Run: `pwsh -File .codex/skills/dotnet-build-diagnostics/scripts/run-build-diagnostics.ps1`
- dotnet-test-triage (only when tests are involved or the build touches test projects): Capture failing tests and a rerun filter. Run: `bash .codex/skills/dotnet-test-triage/scripts/run-test-triage.sh`
- dotnet-format-analyzers (only before finalizing a PR / when touching C# code style): Verify formatting and analyzers. Run: `pwsh -File .codex/skills/dotnet-format-analyzers/scripts/run-format-analyzers.ps1`
- dotnet-symbol-grep-recipes: Use rg navigation recipes during investigation. Run: `rg -n "\b(class|interface|record|struct)\s+<Name>\b" -g "*.cs"`

## LLM Context File (llms.txt)
- Read `llms.txt` at the start of any task to understand the repository's curated map and key references.
- Keep `llms.txt` up to date whenever repository structure, documentation, or core workflows change.

Decision checklist:
- If the task mentions build failures or compiler errors, run dotnet-build-diagnostics.
- If the task mentions failing tests or modifying code with tests, run dotnet-test-triage.
- If adding/modifying C# files, run dotnet-format-analyzers before final output.

Expected artifacts:
- `artifacts/codex/solution-map.json` and `artifacts/codex/solution-map.md`
- `artifacts/codex/build.binlog` and `artifacts/codex/build-summary.txt`
- `artifacts/codex/test-failures.md` and `artifacts/codex/test-filter.txt`
- `artifacts/codex/format-report.txt` (if applicable)

## Project Structure & Module Organization
The core libraries live under `src/`, with `Bravellian.Platform` as the main package and `Bravellian.Platform.Modularity.*` for modularity layers. SQL Server artifacts and schemas are under `src/Bravellian.Platform.SqlServer/Database/`. Tests live in `tests/Bravellian.Platform.Tests/`. Supporting docs are in `docs/` and `specs/`, scripts in `scripts/`, and assets in `assets/`. The primary solution files are `Bravellian.Platform.slnx` and `Bravellian.Platform.CI.slnx` (used by CI).

## Build, Test, and Development Commands
- Before running build or test commands, complete the "Early Repo Orientation (run first)" section.
- `dotnet restore Bravellian.Platform.CI.slnx` restores dependencies.
- `dotnet tool restore` installs local tools (for example, `sqlpackage`).
- `dotnet build Bravellian.Platform.CI.slnx -c Release` builds all projects.
- `dotnet test Bravellian.Platform.CI.slnx -c Release` runs the full test suite.
- `dotnet test --filter "Category!=Integration"` runs fast unit tests only.
- `dotnet test --filter "Category=Integration"` runs Docker-backed integration tests.
- `dotnet format --verify-no-changes Bravellian.Platform.CI.slnx` enforces formatting in CI.
- `dotnet pack Bravellian.Platform.CI.slnx -c Release -o ./nupkgs` produces NuGet packages.

## Coding Style & Naming Conventions
Formatting is driven by `.editorconfig`: 4-space indentation for C#, CRLF line endings, and trimmed trailing whitespace. JSON and Markdown use 2-space indentation. StyleCop settings live in `stylecop.json`; analyzers include Roslynator, Meziantou, and BannedApiAnalyzers. Prefer PascalCase for public types/members and camelCase for locals/parameters/fields. Use explicit `StringComparison` values as required by analyzer rules.

## Testing Guidelines
Tests use xUnit v3 with Shouldly and NSubstitute. Integration tests use Docker SQL Server via Testcontainers; tag them with `[Trait("Category", "Integration")]` and `[Trait("RequiresDocker", "true")]`, and place them in the shared `SqlServerCollection` for container reuse. Unit tests should be tagged `[Trait("Category", "Unit")]`. See `tests/Bravellian.Platform.Tests/README.md` for test patterns.

## Commit & Pull Request Guidelines
Commit messages follow an imperative, sentence-case style (for example, "Add ...") and often include a PR reference in parentheses like `(#123)`. For PRs, include a concise description of changes, relevant testing notes (commands and filters used), and doc updates or screenshots when behavior or APIs change. Link related issues where applicable.

## Configuration & Tooling Notes
The repo pins the .NET SDK in `global.json` (currently 10.0.100). If builds fail locally, verify the SDK version and run `dotnet tool restore` before building or testing.
