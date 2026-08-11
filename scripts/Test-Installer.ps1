[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()

function Require-File([string]$relativePath) {
    $path = Join-Path $repository $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("Missing installer file: $relativePath")
    }
    return $path
}

function Require-Text([string]$path, [string]$pattern, [string]$message) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -notmatch $pattern) { $failures.Add($message) }
}

function Reject-Text([string]$path, [string]$pattern, [string]$message) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    $content = Get-Content -LiteralPath $path -Raw
    if ($content -match $pattern) { $failures.Add($message) }
}

$installer = Require-File 'installer/LilacMacro.iss'
$builder = Require-File 'scripts/Build-Installer.ps1'
$buildProperties = Require-File 'Directory.Build.props'
$verbPolicy = Require-File 'src/LilacMacro.Core/LocalSession/LocalSessionSetupVerbPolicy.cs'
$setupProject = Require-File 'src/LilacMacro.SessionSetup/LilacMacro.SessionSetup.csproj'
$setupManifest = Require-File 'src/LilacMacro.SessionSetup/app.manifest'
$workerProject = Require-File 'src/LilacMacro.SessionWorker/LilacMacro.SessionWorker.csproj'
$payloadPath = Require-File 'third_party/termwrap/v0.6/payload.json'
$license = Require-File 'third_party/termwrap/v0.6/LICENSE.txt'
$zydisLicense = Require-File 'third_party/termwrap/v0.6/ZYDIS-LICENSE.txt'
$nativeDll = Require-File 'third_party/termwrap/v0.6/x64/TermWrap.dll'
$nativeDecoder = Require-File 'third_party/termwrap/v0.6/x64/Zydis.dll'

Require-Text $installer 'DefaultDirName=\{autopf\}\\LilacMacro' 'Installer must target Program Files.'
Require-Text $installer 'PrivilegesRequired=admin' 'Installer must require elevation for lifecycle cleanup.'
Require-Text $installer 'LilacMacro\.SessionSetup\.exe' 'Installer must include the elevated setup helper.'
Require-Text $installer "'repair'" 'Installer upgrade must invoke the repair verb.'
Require-Text $installer 'runner unavailable until Repair succeeds' 'Optional runner migration failure must leave the application upgrade usable.'
Reject-Text $installer 'existing local runner could not be migrated' 'Optional runner migration must not abort the application upgrade.'
Require-Text $installer "'uninstall-cleanup'" 'Installer uninstall must invoke cleanup before deleting binaries.'
Require-Text $installer 'third_party\\termwrap\\v0\.6' 'Installer must bundle the pinned TermWrap payload.'
Require-Text $installer 'NOTICE\.md' 'Installer must include dependency notices.'

Require-Text $builder 'dotnet publish' 'Installer build must publish through dotnet.'
Require-Text $builder '--locked-mode' 'Installer build must use locked dependency resolution.'
Require-Text $builder 'dotnet restore[^\r\n]+--locked-mode' 'Installer build must perform an explicit locked restore.'
Require-Text $builder 'dotnet publish[^\r\n]+--no-restore' 'Installer publishes must reuse the locked restore.'
Reject-Text $builder 'dotnet publish[^\r\n]+--locked-mode' 'Do not pass restore-only --locked-mode to dotnet publish.'
Require-Text $builder 'LOCALAPPDATA.*Inno Setup 6.*ISCC\.exe' 'Installer build must find a per-user Inno Setup installation.'
Require-Text $buildProperties '<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>' 'Locked restore must include the installer runtime graph.'
Require-Text $builder '-c Release' 'Installer build must publish Release binaries.'
Require-Text $builder 'Release installers require -CertificateThumbprint' 'Release installer must require a certificate.'
Require-Text $builder 'LilacMacro-Setup\.exe' 'Installer output must use the canonical executable name.'
Require-Text $builder 'https://timestamp\.digicert\.com' 'Code signing must use an HTTPS timestamp service.'

foreach ($verb in @('install', 'repair', 'remove', 'uninstall-cleanup')) {
    Require-Text $verbPolicy ('"' + [Regex]::Escape($verb) + '"') "Setup helper allowlist is missing verb: $verb"
}
Require-Text $setupProject '<OutputType>WinExe</OutputType>' 'Session setup must be a windowless executable.'
Require-Text $setupManifest 'requireAdministrator' 'Session setup must request elevation through its manifest.'
Require-Text $workerProject '<OutputType>WinExe</OutputType>' 'Session worker must be a windowless executable.'

if (Test-Path -LiteralPath $payloadPath -PathType Leaf) {
    try {
        $payload = Get-Content -LiteralPath $payloadPath -Raw | ConvertFrom-Json
        if ($payload.schema_version -ne 1 -or $payload.version -ne '0.6') {
            $failures.Add('TermWrap payload manifest has an unsupported schema or version.')
        }
        $manifestPaths = @($payload.files | ForEach-Object { [string]$_.relative_path })
        foreach ($requiredPath in @('x64/TermWrap.dll', 'x64/Zydis.dll')) {
            if ($manifestPaths -notcontains $requiredPath) {
                $failures.Add("TermWrap payload manifest is missing required file: $requiredPath")
            }
        }
        foreach ($file in $payload.files) {
            $path = Join-Path (Split-Path $payloadPath) ([string]$file.relative_path)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $failures.Add("TermWrap payload file is missing: $($file.relative_path)")
                continue
            }
            $actual = Get-Item -LiteralPath $path
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            if ($actual.Length -ne [long]$file.size) {
                $failures.Add("TermWrap payload size mismatch: $($file.relative_path)")
            }
            if ($hash -ne [string]$file.sha256) {
                $failures.Add("TermWrap payload hash mismatch: $($file.relative_path)")
            }
        }
    }
    catch {
        $failures.Add("TermWrap payload manifest is invalid: $($_.Exception.Message)")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'Installer policy validation passed.'
