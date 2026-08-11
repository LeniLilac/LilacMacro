[CmdletBinding()]
param(
    [string]$Version,
    [string]$CertificateThumbprint,
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
if (-not $UnsignedDevelopmentBuild -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Release installers require -CertificateThumbprint. Use -UnsignedDevelopmentBuild only for local validation.'
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
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('LilacMacro-installer-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $temporary 'publish'
$output = Join-Path $temporary 'output'

function Invoke-Publish([string]$project) {
    & dotnet publish (Join-Path $repository $project) -c Release --nologo --no-restore `
        -r win-x64 --self-contained true "-p:Version=$Version" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}

function Find-SignTool {
    $direct = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($direct) { return $direct.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kits)) { return $null }
    return Get-ChildItem -LiteralPath $kits -Filter signtool.exe -Recurse |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

function Sign-File([string]$path, [string]$signTool) {
    & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /td SHA256 `
        /tr https://timestamp.digicert.com $path
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $path" }
}

try {
    New-Item -ItemType Directory -Path $publish, $output | Out-Null
    & dotnet restore (Join-Path $repository 'LilacMacro.slnx') --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Locked solution restore failed.' }
    Invoke-Publish 'src\LilacMacro.App\LilacMacro.App.csproj'
    Invoke-Publish 'src\LilacMacro.SessionSetup\LilacMacro.SessionSetup.csproj'
    Invoke-Publish 'src\LilacMacro.SessionWorker\LilacMacro.SessionWorker.csproj'

    $required = @('LilacMacro.exe', 'LilacMacro.SessionSetup.exe', 'LilacMacro.SessionWorker.exe')
    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing published file: $name" }
    }

    $signTool = if ($UnsignedDevelopmentBuild) { $null } else { Find-SignTool }
    if (-not $UnsignedDevelopmentBuild -and -not $signTool) { throw 'Windows SignTool was not found.' }
    if ($signTool) {
        Get-ChildItem -LiteralPath $publish -File |
            Where-Object {
                $_.Name -like 'LilacMacro*.exe' -or $_.Name -like 'LilacMacro*.dll'
            } |
            ForEach-Object { Sign-File $_.FullName $signTool }
    }

    & $iscc "/DSourceRoot=$repository" "/DPublishRoot=$publish" `
        "/DOutputRoot=$output" "/DAppVersion=$Version" `
        (Join-Path $repository 'installer\LilacMacro.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $setup = Join-Path $output 'LilacMacro-Setup.exe'
    if ($signTool) { Sign-File $setup $signTool }

    New-Item -ItemType Directory -Path $artifact | Out-Null
    $artifactSetup = Join-Path $artifact 'LilacMacro-Setup.exe'
    Move-Item -LiteralPath $setup -Destination $artifactSetup
    $setupHash = (Get-FileHash -LiteralPath $artifactSetup -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(
        (Join-Path $artifact 'LilacMacro-Setup.exe.sha256'),
        "$setupHash  LilacMacro-Setup.exe`n",
        [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE.md') -Destination (Join-Path $artifact 'LICENSE.md')
    Copy-Item -LiteralPath (Join-Path $repository 'NOTICE.md') -Destination (Join-Path $artifact 'NOTICE.md')
    [IO.File]::WriteAllLines((Join-Path $artifact 'BUILD-INFO.txt'), @(
        "artifact=macro-installer", "version=$Version",
        "signed=$(((-not $UnsignedDevelopmentBuild).ToString()).ToLowerInvariant())",
        "built_utc=$([DateTimeOffset]::UtcNow.ToString('O'))"
    ), [Text.UTF8Encoding]::new($false))
    Write-Output $artifactSetup
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
