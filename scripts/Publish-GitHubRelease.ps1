[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$artifact = Join-Path $repository "artifacts\macro-$Version-installer"
$buildInfo = Join-Path $artifact 'BUILD-INFO.txt'
$assetNames = @('LilacMacro-Setup.exe', 'LilacMacro-Setup.exe.sha256', 'LICENSE.md', 'NOTICE.md')

if (-not (Get-Command gh.exe -ErrorAction SilentlyContinue)) { throw 'GitHub CLI is required.' }
if ((& git -C $repository status --porcelain)) { throw 'Release publishing requires a clean worktree.' }
if (-not (Test-Path -LiteralPath $buildInfo)) { throw "Release build metadata is missing: $buildInfo" }
if ((Get-Content -LiteralPath $buildInfo) -notcontains 'signed=true') {
    throw 'Only a signed production installer can be published.'
}

$assets = foreach ($name in $assetNames) {
    $path = Join-Path $artifact $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $name" }
    $path
}
$installerHash = (Get-FileHash -LiteralPath $assets[0] -Algorithm SHA256).Hash
$checksum = (Get-Content -LiteralPath $assets[1] -Raw).Trim()
if ($checksum -cne "$installerHash  LilacMacro-Setup.exe") {
    throw 'The release checksum manifest does not match the installer.'
}

$tag = "v$Version"
$arguments = @(
    'release', 'create', $tag,
    '--repo', 'LeniLilac/LilacMacro',
    '--title', "LilacMacro $tag",
    '--generate-notes',
    '--verify-tag'
)
if ($Prerelease) { $arguments += '--prerelease' }
$arguments += $assets
& gh @arguments
if ($LASTEXITCODE -ne 0) { throw "GitHub release creation failed for $tag." }
