param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\\..\\..\\..")
$artifactsDir = Join-Path $repoRoot "artifacts\\codex"
$resultsDir = Join-Path $artifactsDir "test-results"
$outputMd = Join-Path $artifactsDir "test-failures.md"
$outputFilter = Join-Path $artifactsDir "test-filter.txt"
$parser = Join-Path $repoRoot ".codex\\skills\\dotnet-test-triage\\scripts\\collect-test-failures.py"

New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

$loggerArgs = @("--logger", "trx", "--results-directory", $resultsDir)
$allArgs = @()
if ($Args) {
    $allArgs += $Args
}
$allArgs += $loggerArgs

function Quote-Arg {
    param([string]$Value)
    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '""') + '"'
    }
    return $Value
}

$exitCode = 0
if ($env:DOTNET_TEST_CMD) {
    $quotedArgs = $allArgs | ForEach-Object { Quote-Arg $_ }
    $cmdLine = ($env:DOTNET_TEST_CMD, ($quotedArgs -join " ")) -join " "
    & cmd.exe /c $cmdLine
    $exitCode = $LASTEXITCODE
} else {
    & dotnet test @allArgs
    $exitCode = $LASTEXITCODE
}

function Write-FailureArtifacts {
    param(
        [string]$ResultsDir,
        [string]$OutputMd,
        [string]$OutputFilter
    )

    $trx = Get-ChildItem -Path $ResultsDir -Filter *.trx -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $trx) {
        Set-Content -Path $OutputMd -Value "# Test Failures`n`nNo TRX files found in $ResultsDir."
        Set-Content -Path $OutputFilter -Value ""
        return
    }

    [xml]$xml = Get-Content $trx.FullName
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

    $failed = $xml.SelectNodes("//t:UnitTestResult[@outcome='Failed']", $ns)
    if (-not $failed -or $failed.Count -eq 0) {
        Set-Content -Path $OutputMd -Value "# Test Failures`n`nNo failing tests detected."
        Set-Content -Path $OutputFilter -Value ""
        return
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Test Failures")
    $lines.Add("")

    $filters = New-Object System.Collections.Generic.List[string]

    foreach ($result in $failed) {
        $name = $result.GetAttribute("testName")
        $errorMessage = ""
        $stackTrace = ""
        $outputNode = $result.SelectSingleNode("t:Output", $ns)
        if ($outputNode) {
            $errorInfo = $outputNode.SelectSingleNode("t:ErrorInfo", $ns)
            if ($errorInfo) {
                $messageNode = $errorInfo.SelectSingleNode("t:Message", $ns)
                $stackNode = $errorInfo.SelectSingleNode("t:StackTrace", $ns)
                if ($messageNode) { $errorMessage = $messageNode.InnerText.Trim() }
                if ($stackNode) { $stackTrace = $stackNode.InnerText.Trim() }
            }
        }

        $lines.Add("## $name")
        if ($errorMessage) {
            $lines.Add("")
            $lines.Add("Message:")
            $lines.Add('```')
            $lines.Add($errorMessage)
            $lines.Add('```')
        }
        if ($stackTrace) {
            $lines.Add("")
            $lines.Add("Stack:")
            $lines.Add('```')
            $lines.Add($stackTrace)
            $lines.Add('```')
        }
        $lines.Add("")

        if ($name) {
            $filters.Add("FullyQualifiedName=$name")
        }
    }

    Set-Content -Path $OutputMd -Value ($lines -join "`n")
    Set-Content -Path $OutputFilter -Value ($filters -join "|")
}

$python = Get-Command python3 -ErrorAction SilentlyContinue
if (-not $python) {
    $python = Get-Command python -ErrorAction SilentlyContinue
}
if (-not $python) {
    $python = Get-Command py -ErrorAction SilentlyContinue
    if ($python) {
        & $python -3 $parser $resultsDir $outputMd $outputFilter
    } else {
        Write-FailureArtifacts -ResultsDir $resultsDir -OutputMd $outputMd -OutputFilter $outputFilter
    }
} else {
    & $python $parser $resultsDir $outputMd $outputFilter
}

exit $exitCode
