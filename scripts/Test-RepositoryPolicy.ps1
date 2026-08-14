[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repositoryRoot 'eng\repository-policy.json'
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

Push-Location $repositoryRoot
try {
    $sourceFiles = & git -C $repositoryRoot ls-files --cached --others --exclude-standard -- '*.cs' '*.xaml' '*.ps1' '*.py' ':(glob)**/AGENTS.md'
    if ($LASTEXITCODE -ne 0) { throw 'Git failed while enumerating repository source files.' }

    foreach ($relativePath in $sourceFiles) {
        $normalized = $relativePath.Replace('\', '/')
        $fullPath = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
        $lineCount = (Get-Content -LiteralPath $fullPath | Measure-Object -Line).Lines

        if ($normalized -eq 'AGENTS.md' -or $normalized.EndsWith('/AGENTS.md', [StringComparison]::OrdinalIgnoreCase)) {
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

    $lightThemePath = Join-Path $repositoryRoot 'src\LilacMacro.App\Themes\ThemeColors.Light.xaml'
    $darkThemePath = Join-Path $repositoryRoot 'src\LilacMacro.App\Themes\ThemeColors.Dark.xaml'
    $resourceKeyPattern = 'x:Key="([^"]+)"'
    $brushPattern = '<SolidColorBrush\s+x:Key="([^"]+)"'
    $lightTheme = Get-Content -LiteralPath $lightThemePath -Raw
    $darkTheme = Get-Content -LiteralPath $darkThemePath -Raw
    $lightKeys = [regex]::Matches($lightTheme, $resourceKeyPattern) |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $darkKeys = [regex]::Matches($darkTheme, $resourceKeyPattern) |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    if (Compare-Object $lightKeys $darkKeys) {
        $failures.Add('Light and dark theme dictionaries must define the same semantic resource keys.')
    }
    $lightBrushes = [regex]::Matches($lightTheme, $brushPattern) |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $darkBrushes = [regex]::Matches($darkTheme, $brushPattern) |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $themeDifference = Compare-Object $lightBrushes $darkBrushes
    if ($themeDifference) {
        $failures.Add('Light and dark theme dictionaries must define the same semantic brush keys.')
    }

    $themeBrushPattern = '\{StaticResource\s+(' +
        (($lightBrushes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\}'
    foreach ($relativePath in $sourceFiles | Where-Object { $_.EndsWith('.xaml', [StringComparison]::OrdinalIgnoreCase) }) {
        $normalized = $relativePath.Replace('\', '/')
        if ($normalized -like 'src/LilacMacro.App/Themes/ThemeColors.*.xaml') { continue }
        $contents = Get-Content -LiteralPath (Join-Path $repositoryRoot $relativePath) -Raw
        if ($contents -match $themeBrushPattern) {
            $failures.Add("$normalized uses static theme brush '$($Matches[1])'; use DynamicResource so live theme changes propagate.")
        }
        if ($contents -match '(Background|Foreground|BorderBrush|Fill|Stroke)="(White|Black|#[0-9A-Fa-f]{3,8})"') {
            $failures.Add("$normalized hard-codes $($Matches[1]) '$($Matches[2])'; use a semantic dynamic theme resource.")
        }
    }

    foreach ($relativePath in $sourceFiles | Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) }) {
        $normalized = $relativePath.Replace('\', '/')
        if (!$normalized.StartsWith('src/LilacMacro.App/', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $contents = Get-Content -LiteralPath (Join-Path $repositoryRoot $relativePath) -Raw
        if ($contents -match '\.(Background|Foreground|BorderBrush)\s*=\s*(?:\(Brush\)\s*)?(?:FindResource|TryFindResource)') {
            $failures.Add("$normalized assigns a resolved theme brush to $($Matches[1]); use SetResourceReference so live theme changes propagate.")
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

Write-Output 'Repository policy passed: file limits and live theme-resource rules are satisfied.'

& (Join-Path $PSScriptRoot 'Test-RuntimeEvidence.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
