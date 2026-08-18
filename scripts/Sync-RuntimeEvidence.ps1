[CmdletBinding()]
param(
    [string]$DatasetRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'LilacMacro Datasets')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$specPath = Join-Path $repository 'eng\runtime-evidence.json'
$outputRoot = Join-Path $repository 'src\LilacMacro.App\Assets\RuntimeEvidence'
$spec = Get-Content -LiteralPath $specPath -Raw | ConvertFrom-Json

function Set-MinimumPoolMatches([object]$annotation, [int]$value) {
    if ($null -ne $annotation.PSObject.Properties['minimum_pool_matches']) {
        $annotation.minimum_pool_matches = $value
    } else {
        $annotation | Add-Member -NotePropertyName minimum_pool_matches -NotePropertyValue $value
    }
}

function Get-PoolPhraseCount([object[]]$annotations) {
    return @($annotations | ForEach-Object { @($_.ocr_trials) } |
        ForEach-Object { @($_.regions) } |
        Where-Object {
            $null -ne $_.PSObject.Properties['evidence_role'] -and
            [string]$_.evidence_role -eq 'pool'
        } | ForEach-Object {
            ([string]$_.text).ToLowerInvariant() -replace '[^a-z0-9]', ''
        } | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique).Count
}

function Convert-ToPublicManifestValue([object]$value, [string]$propertyName = '') {
    if ($null -eq $value) { return $null }
    if ($propertyName -match '(?i)_at_utc$' -and $value -is [DateTime]) {
        return $value.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($propertyName -match '(?i)_at_utc$' -and $value -is [DateTimeOffset]) {
        return $value.ToUniversalTime().ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($value -is [string]) {
        $public = $value `
            -replace '(?i)[A-Z]:\\Users\\[^\\\r\n"]+', '<local-user-root>' `
            -replace '(?i)[A-Z]:/Users/[^/\r\n"]+', '<local-user-root>'
        if ($propertyName -match '(?i)_at_utc$') {
            $parsed = [DateTimeOffset]::MinValue
            if ([DateTimeOffset]::TryParse(
                $public,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,
                [ref]$parsed)) {
                return $parsed.ToUniversalTime().ToString('O')
            }
        }
        return $public
    }
    if ($value -is [Collections.IList]) {
        for ($index = 0; $index -lt $value.Count; $index++) {
            $value[$index] = Convert-ToPublicManifestValue $value[$index] $propertyName
        }
        return ,$value
    }
    foreach ($property in @($value.PSObject.Properties | Where-Object MemberType -eq 'NoteProperty')) {
        $property.Value = Convert-ToPublicManifestValue $property.Value $property.Name
    }
    return $value
}

if ($spec.schema_version -ne 1) { throw 'Unsupported runtime-evidence specification.' }
$resolvedRoot = [IO.Path]::GetFullPath($DatasetRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Dataset root does not exist: $resolvedRoot"
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

foreach ($entry in $spec.datasets) {
    $name = [string]$entry.name
    if ([IO.Path]::GetFileName($name) -ne $name) { throw "Invalid dataset name: $name" }
    $source = Join-Path $resolvedRoot $name
    $manifestPath = Join-Path $source 'dataset.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Runtime evidence dataset is missing: $name"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $selected = [Collections.Generic.List[object]]::new()
    $sourceFrameValues = @($entry.source_frames | ForEach-Object { [int]$_ })
    foreach ($sourceFrame in @($entry.source_frames)) {
        $index = [int]$sourceFrame - 1
        if ($index -lt 0 -or $index -ge $manifest.frames.Count) {
            throw "$name has no source frame $sourceFrame."
        }
        $selected.Add($manifest.frames[$index])
    }

    $labels = if ($null -ne $entry.PSObject.Properties['labels']) { @($entry.labels) } else { @() }
    foreach ($label in $labels) {
        if ($null -ne $label.PSObject.Properties['global_group_id']) {
            $groupId = [string]$label.global_group_id
            $matches = @($selected | ForEach-Object { @($_.annotations) } |
                Where-Object { [string]$_.global_group_id -eq $groupId })
            if ($matches.Count -ne $selected.Count) {
                throw "$name global annotation $groupId was not found on every selected frame."
            }
            $matches | ForEach-Object { $_.label = [string]$label.label }
            continue
        }

        $sourceFrame = [int]$label.source_frame
        $selectedIndex = [Array]::IndexOf($sourceFrameValues, $sourceFrame)
        if ($selectedIndex -lt 0) { throw "$name label references unselected frame $sourceFrame." }
        $annotationIndex = [int]$label.annotation_index
        $annotations = @($selected[$selectedIndex].annotations)
        if ($annotationIndex -lt 0 -or $annotationIndex -ge $annotations.Count) {
            throw "$name frame $sourceFrame has no annotation $annotationIndex."
        }
        $annotations[$annotationIndex].label = [string]$label.label
    }

    $additions = if ($null -ne $entry.PSObject.Properties['add_annotations']) {
        @($entry.add_annotations)
    } else { @() }
    foreach ($addition in $additions) {
        $sourceFrame = [int]$addition.source_frame
        $selectedIndex = [Array]::IndexOf($sourceFrameValues, $sourceFrame)
        if ($selectedIndex -lt 0) { throw "$name addition references unselected frame $sourceFrame." }
        $bounds = $addition.bounds
        $selected[$selectedIndex].annotations += [pscustomobject]@{
            id = [string]$addition.id
            global_group_id = $null
            bounds = [pscustomobject]@{
                x = [int]$bounds.x
                y = [int]$bounds.y
                width = [int]$bounds.width
                height = [int]$bounds.height
            }
            label = [string]$addition.label
            notes = [string]$addition.notes
            minimum_pool_matches = 0
            ocr_trials = @()
        }
    }

    $globalGroups = @($selected | ForEach-Object { @($_.annotations) } |
        Where-Object {
            $null -ne $_.PSObject.Properties['global_group_id'] -and
            $null -ne $_.global_group_id
        } | Group-Object global_group_id)
    foreach ($group in $globalGroups) {
        $first = $group.Group[0]
        $firstMinimum = if ($null -ne $first.PSObject.Properties['minimum_pool_matches']) {
            [int]$first.minimum_pool_matches
        } else { 0 }
        $signature = '{0},{1},{2},{3}|{4}|{5}|{6}' -f
            $first.bounds.x, $first.bounds.y, $first.bounds.width, $first.bounds.height,
            $first.label, $first.notes, $firstMinimum
        $onePerFrame = @($selected | ForEach-Object {
            @($_.annotations | Where-Object {
                $null -ne $_.PSObject.Properties['global_group_id'] -and
                $_.global_group_id -eq $group.Name
            }).Count
        } | Where-Object { $_ -ne 1 }).Count -eq 0
        $sharedFields = @($group.Group | Where-Object {
            $minimum = if ($null -ne $_.PSObject.Properties['minimum_pool_matches']) {
                [int]$_.minimum_pool_matches
            } else { 0 }
            ('{0},{1},{2},{3}|{4}|{5}|{6}' -f
                $_.bounds.x, $_.bounds.y, $_.bounds.width, $_.bounds.height,
                $_.label, $_.notes, $minimum) -ne $signature
        }).Count -eq 0
        if (-not $onePerFrame -or -not $sharedFields) {
            $group.Group | ForEach-Object { $_.global_group_id = $null }
        }
    }

    $allAnnotations = @($selected | ForEach-Object { @($_.annotations) })
    $remainingGroups = @($allAnnotations | Where-Object {
        $null -ne $_.PSObject.Properties['global_group_id'] -and
        $null -ne $_.global_group_id
    } | Group-Object global_group_id)
    foreach ($group in $remainingGroups) {
        $poolCount = Get-PoolPhraseCount @($group.Group)
        $requested = if ($null -ne $group.Group[0].PSObject.Properties['minimum_pool_matches']) {
            [int]$group.Group[0].minimum_pool_matches
        } else { 0 }
        $minimum = if ($poolCount -eq 0) { 0 } else {
            [Math]::Min([Math]::Max($(if ($requested -le 0) { 1 } else { $requested }), 1), $poolCount)
        }
        $group.Group | ForEach-Object { Set-MinimumPoolMatches $_ $minimum }
    }
    foreach ($annotation in $allAnnotations | Where-Object {
        $null -eq $_.PSObject.Properties['global_group_id'] -or
        $null -eq $_.global_group_id
    }) {
        $poolCount = Get-PoolPhraseCount @($annotation)
        $requested = if ($null -ne $annotation.PSObject.Properties['minimum_pool_matches']) {
            [int]$annotation.minimum_pool_matches
        } else { 0 }
        $minimum = if ($poolCount -eq 0) { 0 } else {
            [Math]::Min([Math]::Max($(if ($requested -le 0) { 1 } else { $requested }), 1), $poolCount)
        }
        Set-MinimumPoolMatches $annotation $minimum
    }

    $manifest.frames = @($selected)
    $sourceFrames = (@($entry.source_frames) -join ', ')
    $manifest.notes = (($manifest.notes.Trim() + "`r`n`r`n" +
        "Bundled runtime evidence slice. Original dataset: $name. Source frames: $sourceFrames.").Trim())
    $manifest = Convert-ToPublicManifestValue $manifest

    $destination = Join-Path $outputRoot $name
    $images = Join-Path $destination 'images'
    if (Test-Path -LiteralPath $destination) {
        $resolvedDestination = [IO.Path]::GetFullPath($destination)
        $resolvedOutput = [IO.Path]::GetFullPath($outputRoot) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedDestination.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to replace runtime evidence outside $outputRoot."
        }
        Remove-Item -LiteralPath $resolvedDestination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $images -Force | Out-Null

    foreach ($frame in $selected) {
        $sourceImage = Join-Path (Join-Path $source 'images') ([string]$frame.file_name)
        if (-not (Test-Path -LiteralPath $sourceImage -PathType Leaf)) {
            throw "$name is missing image $($frame.file_name)."
        }
        $actualHash = (Get-FileHash -LiteralPath $sourceImage -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$frame.sha256).ToLowerInvariant()) {
            throw "$name image $($frame.file_name) does not match its manifest hash."
        }
        Copy-Item -LiteralPath $sourceImage -Destination (Join-Path $images ([string]$frame.file_name))
    }

    $manifest | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $destination 'dataset.json') -Encoding utf8
}

Write-Output "Bundled $($spec.datasets.Count) runtime evidence slices under $outputRoot."
