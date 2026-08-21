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
$ocrBuilder = Require-File 'scripts/Build-OcrRuntime.ps1'
$ocrSetup = Require-File 'scripts/Setup-Ocr.ps1'
$finalizer = Require-File 'scripts/Finalize-ReleaseArtifact.ps1'
$publisher = Require-File 'scripts/Publish-GitHubRelease.ps1'
$releaseWorkflow = Require-File '.github/workflows/release.yml'
$releaseTrust = Require-File 'eng/release-trust.json'
$buildProperties = Require-File 'Directory.Build.props'
$verbPolicy = Require-File 'src/LilacMacro.Core/LocalSession/LocalSessionSetupVerbPolicy.cs'
$setupProject = Require-File 'src/LilacMacro.SessionSetup/LilacMacro.SessionSetup.csproj'
$setupManifest = Require-File 'src/LilacMacro.SessionSetup/app.manifest'
$workerProject = Require-File 'src/LilacMacro.SessionWorker/LilacMacro.SessionWorker.csproj'
$payloadPath = Require-File 'third_party/termwrap/v0.6/payload.json'
$license = Require-File 'third_party/termwrap/v0.6/LICENSE.txt'
$zydisLicense = Require-File 'third_party/termwrap/v0.6/ZYDIS-LICENSE.txt'
$ocrRuntimeNotice = Require-File 'licenses/OCR-RUNTIME.md'
$nativeDll = Require-File 'third_party/termwrap/v0.6/x64/TermWrap.dll'
$nativeDecoder = Require-File 'third_party/termwrap/v0.6/x64/Zydis.dll'

Require-Text $installer 'DefaultDirName=\{autopf\}\\LilacMacro' 'Installer must target Program Files.'
Require-Text $installer 'PrivilegesRequired=admin' 'Installer must require elevation for lifecycle cleanup.'
Require-Text $installer 'CloseApplications=no' 'Installer must not let Restart Manager stop Remote Desktop Services for the install-once TermWrap payload.'
Reject-Text $installer 'RegisterExtraCloseApplicationsResource' 'Installer shutdown must remain product-bounded instead of registering the full install tree with Restart Manager.'
Require-Text $installer 'taskkill\.exe' 'Manual upgrades must have a bounded cross-session shutdown fallback.'
Require-Text $installer '/F /T /IM' 'Manual upgrade shutdown must terminate only explicit LilacMacro process images.'
Require-Text $installer 'StopUninstallProcesses' 'Uninstall must close LilacMacro UI processes before cleanup.'
Require-Text $installer '/T /IM "' 'Uninstall must request a non-force product-bounded close before force cleanup.'
Require-Text $installer 'relaunch-runners' 'Manual upgrades must relaunch configured runner UIs.'
Require-Text $installer 'RunnerRepairSucceeded := AttemptRunnerRepair' 'Runner relaunch must observe the repair result.'
Require-Text $installer 'if RunnerRepairSucceeded then' 'Runner relaunch must fail closed when repair fails.'
Require-Text $installer 'LilacMacro\.SessionSetup\.exe' 'Installer must include the elevated setup helper.'
Require-Text $installer "'repair'" 'Installer upgrade must invoke the repair verb.'
Require-Text $installer 'UPDATESTATE' 'Installer must accept a bounded coordinated-update state.'
Require-Text $installer 'relaunch-update' 'Installer must relaunch previously active runner UIs after update.'
Require-Text $installer 'GetSHA256OfFile' 'Installer must rehash itself before coordinated shutdown.'
Require-Text $installer 'Cross-account process-handle inspection is not a' 'Coordinated updates must avoid fragile cross-account process inspection.'
Reject-Text $installer 'WaitForUpdateParticipants|OpenProcess@kernel32' 'Installer must not inspect cross-account participant process handles.'
Require-Text $installer 'runner unavailable until Repair succeeds' 'Optional runner migration failure must leave the application upgrade usable.'
Require-Text $installer 'UpdateControl\\update-request\.txt' 'Update shutdown requests must use the dedicated control directory.'
Require-Text $installer 'FileAttributeReparsePoint' 'Update shutdown requests must reject reparse points.'
Require-Text $installer 'icacls\.exe' 'Update shutdown requests must apply an explicit machine ACL.'
Require-Text $installer '\*S-1-5-18:\(OI\)\(CI\)F' 'Update shutdown requests must retain SYSTEM ownership access.'
Require-Text $installer '\*S-1-5-32-544:\(OI\)\(CI\)F' 'Update shutdown requests must retain administrator access.'
Require-Text $installer '\*S-1-5-32-545:\(OI\)\(CI\)RX' 'Update shutdown requests must be read-only to ordinary users.'
Reject-Text $installer 'existing local runner could not be migrated' 'Optional runner migration must not abort the application upgrade.'
Require-Text $installer "'uninstall-cleanup'" 'Installer uninstall must invoke cleanup before deleting binaries.'
Require-Text $installer 'third_party\\termwrap\\v0\.6' 'Installer must bundle the pinned TermWrap payload.'
Require-Text $installer 'third_party\\termwrap\\v0\.6[^\r\n]+onlyifdoesntexist' 'Installer upgrades must not overwrite the loaded versioned TermWrap payload.'
Require-Text $installer 'NOTICE\.md' 'Installer must include dependency notices.'
Require-Text $installer 'licenses\\OCR-RUNTIME\.md' 'Installer must include OCR runtime notices.'
Require-Text $installer '\[InstallDelete\]' 'Installer upgrades must declare legacy asset cleanup.'
Require-Text $installer 'Assets\\RuntimeEvidence' 'Installer upgrades must remove legacy installed runtime evidence.'

Require-Text $builder 'dotnet publish' 'Installer build must publish through dotnet.'
Require-Text $builder '--locked-mode' 'Installer build must use locked dependency resolution.'
Require-Text $builder 'Published output contains repository-only runtime evidence datasets' 'Installer build must reject bundled runtime evidence datasets.'
Require-Text $builder 'Unexpected published asset' 'Installer build must reject non-map screenshot assets.'
Require-Text $builder 'dotnet restore[^\r\n]+--locked-mode' 'Installer build must perform an explicit locked restore.'
Require-Text $builder 'dotnet publish[^\r\n]+--no-restore' 'Installer publishes must reuse the locked restore.'
Reject-Text $builder 'dotnet publish[^\r\n]+--locked-mode' 'Do not pass restore-only --locked-mode to dotnet publish.'
Require-Text $builder 'LOCALAPPDATA.*Inno Setup 6.*ISCC\.exe' 'Installer build must find a per-user Inno Setup installation.'
Require-Text $buildProperties '<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>' 'Locked restore must include the installer runtime graph.'
Require-Text $builder '-c Release' 'Installer build must publish Release binaries.'
Reject-Text $builder 'LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY|Ed25519Signer' 'Installer compilation must never receive or use the release-signing key.'
Require-Text $builder 'source_commit=' 'Release candidates must record their exact source commit.'
Require-Text $builder 'Release candidate builds require a clean source worktree' 'Release candidates must reject dirty source trees.'
Require-Text $builder 'release_manifest_signed=' 'Official installer metadata must record the project signature.'
Require-Text $builder 'LilacMacro-Setup\.exe' 'Installer output must use the canonical executable name.'
Require-Text $builder 'LilacMacro-Setup\.exe\.sha256' 'Installer build must create the release checksum asset.'
Require-Text $builder 'LilacMacro-Release\.json' 'Installer build must create the signed release manifest.'
Reject-Text $builder 'LilacMacro-Release\.sig' 'Unsigned compilation must not create the detached release signature.'
Require-Text $builder 'LICENSE\.md' 'Installer build must create the release license asset.'
Require-Text $builder 'NOTICE\.md' 'Installer build must create the release notice asset.'
Require-Text $builder 'Build-OcrRuntime\.ps1' 'Installer build must bundle the pinned CPU OCR runtime.'
Require-Text $builder 'ocr\\cpu-runtime\.json' 'Installer build must validate the bundled CPU OCR runtime.'
Require-Text $builder 'ocr\\models\\official_models' 'Installer build must validate the bundled OCR models.'
Require-Text $ocrSetup 'BundledPythonPath' 'OCR setup must use the bundled Python runtime when available.'
Require-Text $ocrSetup 'gpu' 'OCR setup must retain the per-user GPU path.'
Require-Text $ocrSetup 'Get-Command python\.exe' 'Interactive OCR setup must support a Python executable on PATH.'
Reject-Text $ocrSetup 'winget\.exe|Python\.Python\.3\.12' 'OCR setup must not invoke a separately elevated Python installer.'
Require-Text $ocrBuilder 'Get-Command python\.exe' 'Bundled OCR build must support the Python executable provisioned by CI.'
Require-Text $ocrBuilder 'LocalApplicationData' 'Bundled OCR builds must keep reusable dependencies outside the repository.'
Require-Text $ocrBuilder "'--cache-dir',[^\r\n]+packageCache" 'Bundled OCR builds must reuse the local Python package cache.'
Require-Text $ocrBuilder 'PADDLE_PDX_CACHE_HOME[^\r\n]+modelCache' 'Bundled OCR builds must reuse the local official-model cache.'
Require-Text $ocrBuilder 'UseLocalRuntimeCache' 'Bundled OCR builds must expose the local assembled-runtime cache only as an explicit option.'
Require-Text $ocrBuilder 'builderSha256' 'The local assembled-runtime cache must be invalidated when its builder changes.'
Require-Text $builder 'UseLocalRuntimeCache:\$UnsignedDevelopmentBuild' 'Only unsigned development installers may reuse the assembled OCR runtime cache.'
Reject-Text $ocrBuilder '--no-cache-dir' 'Bundled OCR builds must not redownload cached Python packages.'
Reject-Text $builder 'CertificateThumbprint|signtool|timestamp\.digicert\.com' 'Official builds must not imply unavailable Authenticode signing.'
Require-Text $finalizer 'LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY' 'The isolated finalizer must require the protected release-signing key.'
Require-Text $finalizer 'Ed25519Signer' 'The isolated finalizer must sign the release manifest with Ed25519.'
Require-Text $finalizer 'sourceCommit' 'The isolated finalizer must bind the signed source commit.'
Reject-Text $finalizer '(?im)^\s*&?\s*(dotnet|gh|ISCC)' 'The secret-bearing finalizer must not launch build, package, or publishing tools.'
Require-Text $publisher 'Ed25519Signer' 'Release publishing must verify the project signature before upload.'
Require-Text $publisher 'signed source commit' 'Release publishing must bind the artifact, source checkout, and release tag.'
Require-Text $publisher 'Unknown publisher' 'Release notes must disclose the unsigned Windows publisher state.'
Reject-Text $publisher 'SHA-256:\s+`\$installerHash' 'Release notes must interpolate the actual installer SHA-256.'
Require-Text $releaseTrust '"algorithm"\s*:\s*"Ed25519"' 'Release trust policy must use Ed25519.'
Require-Text $releaseWorkflow 'innosetup --version=6\.7\.1' 'Release workflow must pin its Inno Setup dependency.'
Require-Text $releaseWorkflow "github\.ref.*refs/heads/main" 'Release workflow must run only from main.'
Require-Text $releaseWorkflow 'LILACMACRO_RELEASE_SIGNING_PUBLIC_KEY' 'Release workflow must bind the committed trust key to the repository variable.'
Require-Text $releaseWorkflow 'RELEASE_VERSION:\s*\$\{\{ inputs\.version \}\}' 'Release inputs must enter PowerShell only through an environment variable.'
Require-Text $releaseWorkflow 'persist-credentials:\s*false' 'Build and signing checkouts must not persist GitHub credentials.'
Require-Text $releaseWorkflow 'environment:\s*release-signing' 'The signing secret must be scoped to the release-signing environment.'
Require-Text $releaseWorkflow '(?s)jobs:.*build:.*sign:.*publish:' 'Release workflow must isolate build, signing, and publishing jobs.'

foreach ($verb in @('install', 'repair', 'remove', 'uninstall-cleanup', 'relaunch-update', 'relaunch-runners')) {
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
