# Observability Analyzers

This toolchain provides Roslyn analyzers for Bravellian audit and observability conventions.

## Analyzer package

The analyzer package (`Bravellian.Platform.Observability.Analyzers`) currently emits:

- `OBS001`: Audit event names should be lowercase and dot-separated.

## Packing for NuGet

```powershell
dotnet pack ./tools/observability/src/Bravellian.Platform.Observability.Analyzers/Bravellian.Platform.Observability.Analyzers.csproj -c Release -o ./nupkgs
```
