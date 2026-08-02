[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repositoryRoot 'eng\repository-policy.json'
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

Push-Location $repositoryRoot
try {
    $sourceFiles = & git -C $repositoryRoot ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.ps1' '*.py' 'AGENTS.md'
    if ($LASTEXITCODE -ne 0) { throw 'Git failed while enumerating repository source files.' }

    foreach ($relativePath in $sourceFiles) {
        $normalized = $relativePath.Replace('\', '/')
        $fullPath = Join-Path $repositoryRoot $relativePath
        $lineCount = (Get-Content -LiteralPath $fullPath | Measure-Object -Line).Lines

        if ($normalized -eq 'AGENTS.md') {
            $category = 'agents'
        }
        elseif ($normalized.StartsWith('tests/', [StringComparison]::OrdinalIgnoreCase)) {
            $category = 'tests'
        }
        elseif ([IO.Path]::GetExtension($normalized) -eq '.ps1') {
            $category = 'scripts'
        }
        else {
            $category = 'production'
        }

        $ordinaryLimit = [int]$policy.line_limits.$category
        $debtProperty = $policy.debt_ceilings.PSObject.Properties[$normalized]
        $effectiveLimit = if ($null -ne $debtProperty) { [int]$debtProperty.Value } else { $ordinaryLimit }
        if ($lineCount -gt $effectiveLimit) {
            $failures.Add("$normalized has $lineCount lines; $category limit is $effectiveLimit.")
        }
        elseif ($null -ne $debtProperty -and $lineCount -le $ordinaryLimit) {
            $failures.Add("$normalized is now within the ordinary $ordinaryLimit-line limit; remove its stale debt ceiling.")
        }
        elseif ($null -ne $debtProperty -and $lineCount -lt $effectiveLimit) {
            $failures.Add("$normalized shrank to $lineCount lines; lower its exact debt ceiling from $effectiveLimit.")
        }
    }
}
finally {
    Pop-Location
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'Repository policy passed: production/source 500, tests 800, scripts 500, AGENTS.md 120 lines.'
