[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repository 'src\LilacMacro.App\Assets\RuntimeEvidence'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repository 'eng\runtime-state-contexts.json'
}

function Get-OptionalProperty([object]$Value, [string]$Name) {
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

$datasets = foreach ($directory in Get-ChildItem -LiteralPath $EvidenceRoot -Directory | Sort-Object Name) {
    $manifestPath = Join-Path $directory.FullName 'dataset.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Runtime evidence dataset is missing dataset.json: $($directory.Name)"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $annotations = [Collections.Generic.List[object]]::new()
    $anchors = [Collections.Generic.List[object]]::new()
    $anchorKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $frame = 0
    foreach ($frameRecord in @($manifest.frames)) {
        $frame++
        foreach ($annotation in @($frameRecord.annotations)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$annotation.label)) {
                $annotations.Add([ordered]@{
                    Frame = $frame
                    Label = [string]$annotation.label
                    Bounds = [ordered]@{
                        X = [int]$annotation.bounds.x
                        Y = [int]$annotation.bounds.y
                        Width = [int]$annotation.bounds.width
                        Height = [int]$annotation.bounds.height
                    }
                })
            }

            foreach ($trial in @($annotation.ocr_trials)) {
                foreach ($region in @($trial.regions)) {
                    if ((Get-OptionalProperty $region 'is_visual_anchor') -ne $true -or
                        [string]::IsNullOrWhiteSpace([string](Get-OptionalProperty $region 'text'))) {
                        continue
                    }
                    $text = ([string](Get-OptionalProperty $region 'text')).Trim()
                    $matchMode = Get-OptionalProperty $region 'match_mode'
                    $selector = Get-OptionalProperty $region 'spatial_selector'
                    $anchorText = Get-OptionalProperty $region 'spatial_anchor_text'
                    $key = ($text.ToLowerInvariant(), $matchMode, $selector, $anchorText) -join "`u{001f}"
                    if (-not $anchorKeys.Add($key)) { continue }
                    $anchors.Add([ordered]@{
                        Text = $text
                        MatchMode = $matchMode
                        SpatialSelector = $selector
                        SpatialAnchorText = $anchorText
                    })
                }
            }
        }
    }

    [ordered]@{
        Name = $directory.Name
        ClientWidth = [int]$manifest.client_width
        ClientHeight = [int]$manifest.client_height
        FrameCount = $frame
        Annotations = $annotations.ToArray()
        VisualAnchors = $anchors.ToArray()
    }
}

$catalog = [ordered]@{
    SchemaVersion = 1
    Datasets = @($datasets)
}
$parent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    New-Item -ItemType Directory -Path $parent | Out-Null
}
$json = ($catalog | ConvertTo-Json -Depth 12) -replace "`r`n", "`n"
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($OutputPath),
    $json + "`n",
    [Text.UTF8Encoding]::new($false))
