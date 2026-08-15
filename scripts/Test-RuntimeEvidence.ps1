[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$specificationPath = Join-Path $repository 'eng\runtime-evidence.json'
$evidenceRoot = Join-Path $repository 'src\LilacMacro.App\Assets\RuntimeEvidence'
$contextCatalog = Join-Path $repository 'eng\runtime-state-contexts.json'
$failures = [Collections.Generic.List[string]]::new()

$specification = Get-Content -LiteralPath $specificationPath -Raw | ConvertFrom-Json
if ($specification.schema_version -ne 1) {
    $failures.Add('eng/runtime-evidence.json must use schema version 1.')
}
$expected = @($specification.datasets | ForEach-Object { [string]$_.name })
$duplicateNames = @($expected | Group-Object | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) {
    $failures.Add("Runtime evidence dataset names must be unique: $($duplicateNames.Name -join ', ').")
}
foreach ($name in $expected) {
    if ([IO.Path]::GetFileName($name) -ne $name) {
        $failures.Add("Runtime evidence dataset name is not a leaf name: $name.")
        continue
    }
    $directory = Join-Path $evidenceRoot $name
    if (-not (Test-Path -LiteralPath (Join-Path $directory 'dataset.json') -PathType Leaf)) {
        $failures.Add("Bundled runtime evidence is missing dataset.json for $name.")
    }
}

$actual = if (Test-Path -LiteralPath $evidenceRoot -PathType Container) {
    @(Get-ChildItem -LiteralPath $evidenceRoot -Directory | ForEach-Object Name)
} else { @() }
foreach ($extra in @($actual | Where-Object { $_ -notin $expected })) {
    $failures.Add("Bundled runtime evidence has an undeclared dataset: $extra.")
}

$temporaryCatalog = Join-Path ([IO.Path]::GetTempPath()) ('LilacMacro-runtime-contexts-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    & (Join-Path $PSScriptRoot 'Export-RuntimeContextCatalog.ps1') `
        -EvidenceRoot $evidenceRoot -OutputPath $temporaryCatalog
    if (-not (Test-Path -LiteralPath $contextCatalog -PathType Leaf)) {
        $failures.Add('eng/runtime-state-contexts.json is missing.')
    } elseif ((Get-Content -LiteralPath $temporaryCatalog -Raw) -cne
              (Get-Content -LiteralPath $contextCatalog -Raw)) {
        $failures.Add('eng/runtime-state-contexts.json is stale; run scripts/Export-RuntimeContextCatalog.ps1.')
    }
} finally {
    if (Test-Path -LiteralPath $temporaryCatalog) {
        Remove-Item -LiteralPath $temporaryCatalog -Force
    }
}

$appProject = Get-Content -LiteralPath (Join-Path $repository 'src\LilacMacro.App\LilacMacro.App.csproj') -Raw
$runtimeProject = Get-Content -LiteralPath (Join-Path $repository 'src\LilacMacro.Runtime\LilacMacro.Runtime.csproj') -Raw
if ($appProject -match 'Content\s+Include="Assets\\RuntimeEvidence') {
    $failures.Add('Runtime evidence datasets must not be copied into application output or publish artifacts.')
}
if ($runtimeProject -notmatch 'EmbeddedResource\s+Include="\.\.\\\.\.\\eng\\runtime-state-contexts\.json"') {
    $failures.Add('The compact runtime state context catalog must be embedded in the application assembly.')
}

$stateCatalogs = @(
    'src\LilacMacro.App\Debugging\DebugWorkflowCatalog.cs',
    'src\LilacMacro.App\Debugging\ExpeditionCheckpointStateCatalog.cs'
)
$referencedDatasets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($relative in $stateCatalogs) {
    $contents = Get-Content -LiteralPath (Join-Path $repository $relative) -Raw
    $stateCount = [regex]::Matches($contents, 'public static readonly DebugStateSpec\s+').Count
    $labelCount = [regex]::Matches($contents, 'RegionLabel\s*:').Count
    if ($stateCount -ne $labelCount) {
        $failures.Add("$($relative.Replace('\', '/')) has $stateCount states but $labelCount explicit RegionLabel values.")
    }
    if ($contents -match 'SpecialFolder\.MyDocuments|LilacMacro Datasets') {
        $failures.Add("$($relative.Replace('\', '/')) reads owner-local datasets instead of bundled evidence.")
    }
    foreach ($match in [regex]::Matches($contents, 'Dataset\("([^"]+)"\)')) {
        [void]$referencedDatasets.Add($match.Groups[1].Value)
    }
}
foreach ($name in $referencedDatasets) {
    if ($name -notin $expected) {
        $failures.Add("Runtime state references unbundled dataset: $name.")
    }
}

$searchSources = @(
    (Join-Path $repository 'src\LilacMacro.App\Runtime'),
    (Join-Path $repository 'src\LilacMacro.Runtime\Normalization')
)
$hardCodedPattern = '(?s)static\s+readonly\s+PixelRect\s+(?!FullClient\b)\w+\s*=\s*new(?:\s+PixelRect)?\s*\('
foreach ($sourceRoot in $searchSources) {
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File) {
        $contents = Get-Content -LiteralPath $file.FullName -Raw
        if ($contents -match $hardCodedPattern) {
            $relative = [IO.Path]::GetRelativePath($repository, $file.FullName).Replace('\', '/')
            $failures.Add("$relative hard-codes a static search PixelRect; add bundled evidence and use RuntimeSearchRegionEvidenceCatalog.")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Runtime evidence policy passed: $($expected.Count) bundled slices and explicit state/search ownership are present."
