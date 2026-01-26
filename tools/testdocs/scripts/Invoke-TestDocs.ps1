[CmdletBinding()]
param(
    [switch]$Strict,
    [double]$MinCompliance = 0.0,
    [string]$RepoRoot
)

$resolvedRepoRoot = if ($RepoRoot) {
    (Resolve-Path $RepoRoot).Path
} else {
    (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

$solutionPath = Join-Path $resolvedRepoRoot "tools/testdocs/TestDocs.sln"
$projectPath = Join-Path $resolvedRepoRoot "tools/testdocs/src/TestDocs.Cli"
$outDir = Join-Path $resolvedRepoRoot "docs/testing/generated"

dotnet build $solutionPath -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$arguments = @(
    "run",
    "--project",
    $projectPath,
    "--configuration",
    "Release",
    "--no-build",
    "--",
    "generate",
    "--repoRoot",
    $resolvedRepoRoot,
    "--outDir",
    $outDir
)

if ($Strict) {
    $arguments += "--strict"
}

if ($PSBoundParameters.ContainsKey("MinCompliance")) {
    $arguments += @("--minCompliance", $MinCompliance.ToString("0.###", [CultureInfo]::InvariantCulture))
}

dotnet @arguments
exit $LASTEXITCODE
