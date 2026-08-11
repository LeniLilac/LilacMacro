[CmdletBinding()]
param(
    [string]$Version,
    [string]$Label
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repository 'Directory.Build.props'
$artifactRoot = Join-Path $repository 'artifacts'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $Version = [string]$props.Project.PropertyGroup.VersionPrefix
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must contain exactly three numeric components, for example 1.0.3."
}

if (-not [string]::IsNullOrWhiteSpace($Label) -and
    $Label -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
    throw "Label must be lowercase kebab-case, for example gpu-warm-team-swap."
}

$suffix = $Version
if (-not [string]::IsNullOrWhiteSpace($Label)) {
    $suffix = "$suffix-$Label"
}

$packages = @(
    [pscustomobject]@{ Name = 'macro'; Launcher = 'LilacMacro.exe' },
    [pscustomobject]@{ Name = 'datasetbuilder'; Launcher = 'LilacMacro.DatasetBuilder.exe' },
    [pscustomobject]@{ Name = 'runtimelab'; Launcher = 'LilacMacro.RuntimeLab.exe' }
    [pscustomobject]@{ Name = 'deepdebugviewer'; Launcher = 'LilacMacro.DeepDebugViewer.exe' }
)

$destinations = @{}
foreach ($package in $packages) {
    $destination = Join-Path $artifactRoot "$($package.Name)-$suffix"
    if (Test-Path -LiteralPath $destination) {
        throw "Artifact already exists: $destination. Increment the version instead of overwriting it."
    }

    $destinations[$package.Name] = $destination
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'LilacMacro-local-publish-' + [Guid]::NewGuid().ToString('N'))
$partialDirectories = [System.Collections.Generic.List[string]]::new()
$launcherNames = @(
    'LilacMacro.exe',
    'LilacMacro.DatasetBuilder.exe',
    'LilacMacro.RuntimeLab.exe'
    'LilacMacro.DeepDebugViewer.exe'
)

try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

    & dotnet publish (Join-Path $repository 'src\LilacMacro.App\LilacMacro.App.csproj') `
        -c Release `
        --nologo `
        "-p:Version=$Version" `
        -o $stagingRoot
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    foreach ($launcherName in $launcherNames) {
        $launcherPath = Join-Path $stagingRoot $launcherName
        if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
            throw "Published launcher is missing: $launcherPath"
        }
    }

    $sourceCommit = (& git -C $repository rev-parse --short=12 HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to identify the source commit.'
    }

    $sourceDirty = -not [string]::IsNullOrWhiteSpace(
        ((& git -C $repository status --porcelain) -join [Environment]::NewLine))
    $builtUtc = [DateTimeOffset]::UtcNow.ToString('O')

    foreach ($package in $packages) {
        $destination = [string]$destinations[$package.Name]
        $partial = "$destination.partial-$([Guid]::NewGuid().ToString('N'))"
        $partialDirectories.Add($partial)
        New-Item -ItemType Directory -Path $partial | Out-Null

        foreach ($item in Get-ChildItem -LiteralPath $stagingRoot -Force) {
            if (-not $item.PSIsContainer -and $launcherNames -contains $item.Name) {
                continue
            }

            Copy-Item -LiteralPath $item.FullName -Destination $partial -Recurse
        }

        Copy-Item -LiteralPath (Join-Path $stagingRoot $package.Launcher) `
            -Destination (Join-Path $partial $package.Launcher)

        $buildInfo = @(
            "artifact=$($package.Name)",
            "version=$Version",
            "label=$Label",
            "primary_executable=$($package.Launcher)",
            "source_commit=$sourceCommit",
            "source_dirty=$($sourceDirty.ToString().ToLowerInvariant())",
            "built_utc=$builtUtc"
        )
        [System.IO.File]::WriteAllLines(
            (Join-Path $partial 'BUILD-INFO.txt'),
            $buildInfo,
            [System.Text.UTF8Encoding]::new($false))

        Move-Item -LiteralPath $partial -Destination $destination
        [void]$partialDirectories.Remove($partial)
        Write-Host "Created $destination"
    }
}
finally {
    foreach ($partial in $partialDirectories) {
        if (Test-Path -LiteralPath $partial) {
            Remove-Item -LiteralPath $partial -Recurse -Force
        }
    }

    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
