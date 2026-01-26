[CmdletBinding()]
param(
    [string]$StatsPath,
    [string]$RepoRoot
)

$resolvedRepoRoot = if ($RepoRoot) {
    (Resolve-Path $RepoRoot).Path
} else {
    (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

$resolvedStatsPath = if ($StatsPath) {
    (Resolve-Path $StatsPath).Path
} else {
    (Resolve-Path (Join-Path $resolvedRepoRoot "docs/testing/generated/stats.json")).Path
}

$stats = Get-Content -Raw $resolvedStatsPath | ConvertFrom-Json
$total = $stats.summary.total
$compliant = $stats.summary.compliant
$missing = $stats.summary.missingRequired
$invalid = $stats.summary.invalidFormat

if ($total -eq 0) {
    $compliance = "100.0%"
} else {
    $compliance = [string]::Format([CultureInfo]::InvariantCulture, "{0:P1}", ($compliant / $total))
}

Write-Output "Test docs: $total tests, $compliant compliant, $missing missing required, $invalid invalid."
Write-Output "Compliance: $compliance"
