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
$assetNames = @(
    'LilacMacro-Setup.exe',
    'LilacMacro-Setup.exe.sha256',
    'LilacMacro-Release.json',
    'LilacMacro-Release.sig',
    'LICENSE.md',
    'NOTICE.md'
)

if (-not (Get-Command gh.exe -ErrorAction SilentlyContinue)) { throw 'GitHub CLI is required.' }
if ((& git -C $repository status --porcelain)) { throw 'Release publishing requires a clean worktree.' }
if (-not (Test-Path -LiteralPath $buildInfo)) { throw "Release build metadata is missing: $buildInfo" }
$metadata = Get-Content -LiteralPath $buildInfo
if ($metadata -notcontains 'release_manifest_signed=true') {
    throw 'Only a project-signed official installer can be published.'
}
$sourceCommitEntry = $metadata | Where-Object { $_ -like 'source_commit=*' } | Select-Object -First 1
$sourceCommit = if ($sourceCommitEntry) {
    $sourceCommitEntry.Substring('source_commit='.Length)
} else {
    [string]::Empty
}
$headCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$' -or $sourceCommit -cne $headCommit) {
    throw 'The signed release artifact was not built from the publishing source commit.'
}

$assets = foreach ($name in $assetNames) {
    $path = Join-Path $artifact $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $name" }
    $path
}
$installer = Join-Path $artifact 'LilacMacro-Setup.exe'
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$checksum = (Get-Content -LiteralPath (Join-Path $artifact 'LilacMacro-Setup.exe.sha256') -Raw).Trim()
if ($checksum -cne "$installerHash  LilacMacro-Setup.exe") {
    throw 'The release checksum manifest does not match the installer.'
}

$trust = Get-Content -LiteralPath (Join-Path $repository 'eng\release-trust.json') -Raw | ConvertFrom-Json
$manifestPath = Join-Path $artifact 'LilacMacro-Release.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$installerFile = Get-Item -LiteralPath $installer
if ($manifest.format -ne 'lilacmacro.release' -or $manifest.schemaVersion -ne 1 -or
    $manifest.keyId -ne $trust.keyId -or $manifest.algorithm -ne 'Ed25519' -or
    $manifest.tag -cne "v$Version" -or $manifest.sourceCommit -cne $sourceCommit -or
    $manifest.installer.name -cne 'LilacMacro-Setup.exe' -or
    [long]$manifest.installer.size -ne $installerFile.Length -or
    ([string]$manifest.installer.sha256).ToUpperInvariant() -cne $installerHash) {
    throw 'The project-signed release manifest does not match the installer artifact.'
}

[xml]$packages = Get-Content -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Raw
$bouncyVersion = [string]($packages.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -eq 'BouncyCastle.Cryptography' }).Version
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else {
    Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}
$assembly = Join-Path $packageRoot "bouncycastle.cryptography\$bouncyVersion\lib\net6.0\BouncyCastle.Cryptography.dll"
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw 'The restored BouncyCastle net6.0 assembly was not found.'
}
Add-Type -Path $assembly
$publicKey = [Org.BouncyCastle.Security.PublicKeyFactory]::CreateKey(
    [Convert]::FromBase64String([string]$trust.publicKeySpkiBase64))
$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
$signature = [Convert]::FromBase64String((Get-Content -LiteralPath (Join-Path $artifact 'LilacMacro-Release.sig') -Raw).Trim())
$verifier = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
$verifier.Init($false, $publicKey)
$verifier.BlockUpdate($manifestBytes, 0, $manifestBytes.Length)
if (-not $verifier.VerifySignature($signature)) { throw 'The project release signature is invalid.' }

$tag = "v$Version"
$tagCommit = (& git -C $repository rev-list -n 1 $tag).Trim()
if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $sourceCommit) {
    throw 'The release tag does not resolve to the signed source commit.'
}
$notes = @"
Public beta for Windows.

LilacMacro is noncommercial, source-available software. The installer is intentionally not Authenticode-signed, so Windows may show **Unknown publisher** and Microsoft Defender SmartScreen may require **More info → Run anyway**. The app and updater verify the project Ed25519 release signature and SHA-256 digest.

Expected installer SHA-256: $installerHash

Review ``LilacMacro-Release.json``, ``LilacMacro-Release.sig``, and ``LilacMacro-Setup.exe.sha256`` before installation. Roblox and Anime Expeditions can change independently, so supervise beta runs and stop immediately if observed behavior differs from the configured plan.
"@
$arguments = @(
    'release', 'create', $tag,
    '--repo', 'LeniLilac/LilacMacro',
    '--title', "LilacMacro $tag public beta",
    '--notes', $notes,
    '--verify-tag'
)
if ($Prerelease) { $arguments += '--prerelease' }
$arguments += $assets
& gh @arguments
if ($LASTEXITCODE -ne 0) { throw "GitHub release creation failed for $tag." }
