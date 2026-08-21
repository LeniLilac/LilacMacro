[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '1.4.2'
$archiveUrl = "https://github.com/AOMediaCodec/libavif/releases/download/v$version/windows-artifacts.zip"
$licenseUrl = "https://raw.githubusercontent.com/AOMediaCodec/libavif/v$version/LICENSE"
$archiveHash = 'CB2D9FEA43DCBAB1D0707E3B37EB7B08070AD2FB60A2C188C39EC12382C0484A'
$licenseHash = '165ABF92CC04B39E80D29CADEA7A6A7E8FDDF59407D4AD2616507A7EBE8216F9'
$toolHashes = @{
    'avifenc.exe' = '42312321ED9F715987CB3623292EAEDC778970B45EFB9B1DE62CE795F61991A0'
    'avifdec.exe' = 'A03664DDFFB9D847EB484AF6CEEFFCE96E03D641DA5C577D26662487DEE689F2'
}
$cacheRoot = Join-Path $env:LOCALAPPDATA "LilacMacro\BuildCache\avif\$version"
$archive = Join-Path $cacheRoot 'windows-artifacts.zip'
$expanded = Join-Path $cacheRoot 'expanded'
$license = Join-Path $cacheRoot 'LICENSE.txt'

function Assert-Hash([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected
}

New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
if (-not (Assert-Hash $archive $archiveHash)) {
    $download = "$archive.download-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $archiveUrl -OutFile $download
        if (-not (Assert-Hash $download $archiveHash)) { throw 'Downloaded libavif archive hash did not match.' }
        Move-Item -LiteralPath $download -Destination $archive -Force
    }
    finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force } }
}
if (-not (Assert-Hash $license $licenseHash)) {
    Invoke-WebRequest -UseBasicParsing -Uri $licenseUrl -OutFile $license
    if (-not (Assert-Hash $license $licenseHash)) { throw 'Downloaded libavif license hash did not match.' }
}
New-Item -ItemType Directory -Path $expanded -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
foreach ($name in $toolHashes.Keys) {
    $path = Join-Path $expanded $name
    if (-not (Assert-Hash $path $toolHashes[$name])) { throw "Pinned libavif tool hash did not match: $name" }
}

$destination = [System.IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in $toolHashes.Keys) {
    Copy-Item -LiteralPath (Join-Path $expanded $name) -Destination (Join-Path $destination $name) -Force
}
Copy-Item -LiteralPath $license -Destination (Join-Path $destination 'LICENSE.txt') -Force
[IO.File]::WriteAllText(
    (Join-Path $destination 'VERSION.txt'),
    "libavif $version`n",
    [Text.UTF8Encoding]::new($false))
