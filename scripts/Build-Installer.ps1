[CmdletBinding()]
param(
    [string]$Version,
    [switch]$UnsignedDevelopmentBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    $Version = [string]$props.Project.PropertyGroup.VersionPrefix
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be semantic x.y.z.' }
$sourceCommit = 'development'
if (-not $UnsignedDevelopmentBuild) {
    if (& git -C $repository status --porcelain) {
        throw 'Release candidate builds require a clean source worktree.'
    }
    $sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Release candidate source commit was unavailable.'
    }
}

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found.' }

$artifact = Join-Path $repository "artifacts\macro-$Version-installer"
if (Test-Path -LiteralPath $artifact) { throw "Artifact already exists: $artifact" }
# Keep the publish root short enough for Inno Setup to process Python package paths.
$temporary = Join-Path $env:USERPROFILE ('lilacmacro-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $temporary 'publish'
$output = Join-Path $temporary 'output'

function Invoke-Publish([string]$project) {
    & dotnet publish (Join-Path $repository $project) -c Release --nologo --no-restore `
        -r win-x64 --self-contained true "-p:Version=$Version" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}

try {
    New-Item -ItemType Directory -Path $publish, $output | Out-Null
    & dotnet restore (Join-Path $repository 'LilacMacro.slnx') --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Locked solution restore failed.' }
    Invoke-Publish 'src\LilacMacro.App\LilacMacro.App.csproj'
    Invoke-Publish 'src\LilacMacro.SessionSetup\LilacMacro.SessionSetup.csproj'
    Invoke-Publish 'src\LilacMacro.SessionWorker\LilacMacro.SessionWorker.csproj'

    & (Join-Path $repository 'scripts\Build-OcrRuntime.ps1') -OutputRoot $publish
    if ($LASTEXITCODE -ne 0) { throw 'Bundled OCR runtime build failed.' }

    $legacyEvidence = Join-Path $publish 'Assets\RuntimeEvidence'
    if (Test-Path -LiteralPath $legacyEvidence) {
        throw 'Published output contains repository-only runtime evidence datasets.'
    }
    $assetRoot = Join-Path $publish 'Assets'
    if (Test-Path -LiteralPath $assetRoot) {
        foreach ($asset in Get-ChildItem -LiteralPath $assetRoot -File -Recurse) {
            $relativeAsset = $asset.FullName.Substring($assetRoot.Length).TrimStart('\')
            if ($relativeAsset -notmatch '^PlacementMaps\\[^\\]+\.jpg$') {
                throw "Unexpected published asset: $relativeAsset"
            }
        }
    }

    foreach ($name in @('LilacMacro.exe', 'LilacMacro.SessionSetup.exe', 'LilacMacro.SessionWorker.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing published file: $name" }
    }
    foreach ($path in @(
        'ocr\python\python.exe',
        'ocr\cpu-runtime.json',
        'ocr\models\official_models')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $path))) {
            throw "Missing bundled OCR runtime file: $path"
        }
    }

    & $iscc "/DSourceRoot=$repository" "/DPublishRoot=$publish" `
        "/DOutputRoot=$output" "/DAppVersion=$Version" `
        (Join-Path $repository 'installer\LilacMacro.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

    New-Item -ItemType Directory -Path $artifact | Out-Null
    $artifactSetup = Join-Path $artifact 'LilacMacro-Setup.exe'
    Move-Item -LiteralPath (Join-Path $output 'LilacMacro-Setup.exe') -Destination $artifactSetup
    $setupFile = Get-Item -LiteralPath $artifactSetup
    $setupHash = (Get-FileHash -LiteralPath $artifactSetup -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(
        (Join-Path $artifact 'LilacMacro-Setup.exe.sha256'),
        "$setupHash  LilacMacro-Setup.exe`n",
        [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE.md') -Destination (Join-Path $artifact 'LICENSE.md')
    Copy-Item -LiteralPath (Join-Path $repository 'NOTICE.md') -Destination (Join-Path $artifact 'NOTICE.md')

    $trustKeyId = 'none'
    $manifestPrepared = $false
    if (-not $UnsignedDevelopmentBuild) {
        $trust = Get-Content -LiteralPath (Join-Path $repository 'eng\release-trust.json') -Raw | ConvertFrom-Json
        if ($trust.format -ne 'lilacmacro.release-trust' -or $trust.schemaVersion -ne 1 -or
            $trust.algorithm -ne 'Ed25519' -or [string]::IsNullOrWhiteSpace([string]$trust.keyId)) {
            throw 'Release trust policy is invalid.'
        }
        $manifest = [ordered]@{
            format = 'lilacmacro.release'
            schemaVersion = 1
            keyId = [string]$trust.keyId
            algorithm = 'Ed25519'
            tag = "v$Version"
            sourceCommit = $sourceCommit
            installer = [ordered]@{
                name = 'LilacMacro-Setup.exe'
                size = $setupFile.Length
                sha256 = $setupHash
            }
        } | ConvertTo-Json -Depth 4 -Compress
        $manifestPath = Join-Path $artifact 'LilacMacro-Release.json'
        [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
        $trustKeyId = [string]$trust.keyId
        $manifestPrepared = $true
    }

    [IO.File]::WriteAllLines((Join-Path $artifact 'BUILD-INFO.txt'), @(
        'artifact=macro-installer', "version=$Version",
        "source_commit=$sourceCommit",
        "source_dirty=$($UnsignedDevelopmentBuild.ToString().ToLowerInvariant())",
        'authenticode_signed=false',
        "release_manifest_prepared=$($manifestPrepared.ToString().ToLowerInvariant())",
        'release_manifest_signed=false',
        "release_trust_key=$trustKeyId",
        "built_utc=$([DateTimeOffset]::UtcNow.ToString('O'))"
    ), [Text.UTF8Encoding]::new($false))
    Write-Output $artifactSetup
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
