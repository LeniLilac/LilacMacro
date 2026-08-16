[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$artifact = Join-Path $repository "artifacts\macro-$Version-installer"
$manifestPath = Join-Path $artifact 'LilacMacro-Release.json'
$signaturePath = Join-Path $artifact 'LilacMacro-Release.sig'
$installerPath = Join-Path $artifact 'LilacMacro-Setup.exe'
$checksumPath = Join-Path $artifact 'LilacMacro-Setup.exe.sha256'
$buildInfoPath = Join-Path $artifact 'BUILD-INFO.txt'
$privateBytes = $null

function Require-File([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release candidate file is missing: $(Split-Path -Leaf $path)"
    }
}

function Import-BouncyCastle {
    [xml]$packages = Get-Content -LiteralPath (Join-Path $repository 'Directory.Packages.props') -Raw
    $version = [string]($packages.Project.ItemGroup.PackageVersion |
        Where-Object { $_.Include -eq 'BouncyCastle.Cryptography' }).Version
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'BouncyCastle package version was not found.' }
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else {
        Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
    }
    $assembly = Join-Path $packageRoot "bouncycastle.cryptography\$version\lib\net6.0\BouncyCastle.Cryptography.dll"
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw 'The restored BouncyCastle net6.0 assembly was not found.'
    }
    Add-Type -Path $assembly
}

try {
    foreach ($path in @($manifestPath, $installerPath, $checksumPath, $buildInfoPath)) {
        Require-File $path
    }
    if (Test-Path -LiteralPath $signaturePath) {
        throw 'The release candidate already contains a signature.'
    }
    $metadata = Get-Content -LiteralPath $buildInfoPath
    if ($metadata -notcontains "version=$Version" -or
        $metadata -notcontains 'source_dirty=false' -or
        $metadata -notcontains 'release_manifest_prepared=true' -or
        $metadata -notcontains 'release_manifest_signed=false') {
        throw 'The release candidate metadata is not ready for signing.'
    }

    $trust = Get-Content -LiteralPath (Join-Path $repository 'eng\release-trust.json') -Raw |
        ConvertFrom-Json
    if ($trust.format -ne 'lilacmacro.release-trust' -or $trust.schemaVersion -ne 1 -or
        $trust.algorithm -ne 'Ed25519' -or [string]::IsNullOrWhiteSpace([string]$trust.keyId)) {
        throw 'Release trust policy is invalid.'
    }
    $installer = Get-Item -LiteralPath $installerPath
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    $checksum = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
    if ($checksum -cne "$installerHash  LilacMacro-Setup.exe") {
        throw 'The release checksum does not match the candidate installer.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $sourceCommit = ($metadata | Where-Object { $_ -like 'source_commit=*' } |
        Select-Object -First 1).Substring('source_commit='.Length)
    if ($manifest.format -ne 'lilacmacro.release' -or $manifest.schemaVersion -ne 1 -or
        $manifest.keyId -cne [string]$trust.keyId -or $manifest.algorithm -ne 'Ed25519' -or
        $manifest.tag -cne "v$Version" -or $manifest.sourceCommit -cne $sourceCommit -or
        $sourceCommit -notmatch '^[0-9a-f]{40}$' -or
        $manifest.installer.name -cne 'LilacMacro-Setup.exe' -or
        [long]$manifest.installer.size -ne $installer.Length -or
        ([string]$manifest.installer.sha256).ToUpperInvariant() -cne $installerHash) {
        throw 'The release manifest does not bind the candidate installer.'
    }

    $privateKeyText = [Environment]::GetEnvironmentVariable('LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY')
    if ([string]::IsNullOrWhiteSpace($privateKeyText)) {
        throw 'The protected release-signing key was not supplied.'
    }
    try { $privateBytes = [Convert]::FromBase64String($privateKeyText) }
    catch [FormatException] { throw 'Release signing private key encoding is invalid.' }
    $privateKeyText = $null
    [Environment]::SetEnvironmentVariable('LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY', $null)

    Import-BouncyCastle
    $privateKey = [Org.BouncyCastle.Security.PrivateKeyFactory]::CreateKey($privateBytes)
    $publicKey = [Org.BouncyCastle.Security.PublicKeyFactory]::CreateKey(
        [Convert]::FromBase64String([string]$trust.publicKeySpkiBase64))
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    $signer = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
    $signer.Init($true, $privateKey)
    $signer.BlockUpdate($manifestBytes, 0, $manifestBytes.Length)
    $signature = $signer.GenerateSignature()
    $verifier = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
    $verifier.Init($false, $publicKey)
    $verifier.BlockUpdate($manifestBytes, 0, $manifestBytes.Length)
    if (-not $verifier.VerifySignature($signature)) {
        throw 'Release signing private key does not match the trusted public key.'
    }
    [IO.File]::WriteAllText(
        $signaturePath,
        [Convert]::ToBase64String($signature) + "`n",
        [Text.Encoding]::ASCII)

    $updatedMetadata = $metadata | ForEach-Object {
        if ($_ -ceq 'release_manifest_signed=false') { 'release_manifest_signed=true' } else { $_ }
    }
    [IO.File]::WriteAllLines($buildInfoPath, $updatedMetadata, [Text.UTF8Encoding]::new($false))
    Write-Output $signaturePath
}
finally {
    if ($privateBytes) { [Array]::Clear($privateBytes, 0, $privateBytes.Length) }
    [Environment]::SetEnvironmentVariable('LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY', $null)
}
