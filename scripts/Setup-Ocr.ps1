[CmdletBinding()]
param(
    [ValidateSet('cpu', 'gpu')]
    [string]$Device = 'cpu',
    [string]$InstallRoot = ''
)

$ErrorActionPreference = 'Stop'

$ocrRoot = if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Join-Path $env:LOCALAPPDATA 'LilacMacro\ocr'
} else {
    [IO.Path]::GetFullPath($InstallRoot)
}
$venvRoot = Join-Path $ocrRoot 'venv'
$venvPython = Join-Path $venvRoot 'Scripts\python.exe'
$runtimeMarker = Join-Path $ocrRoot 'runtime-device.txt'
$profileMarker = Join-Path $ocrRoot 'runtime-profile.json'
$gpuPolicy = Join-Path $PSScriptRoot 'OcrGpuRuntime.ps1'

if (-not (Test-Path -LiteralPath $gpuPolicy)) {
    throw 'LilacMacro could not locate the OCR GPU runtime policy.'
}
. $gpuPolicy

function Find-Python312 {
    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -ne $launcher) {
        $probe = @()
        $probeExitCode = 1
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            # A launcher without Python 3.12 is an expected miss; let the winget fallback handle it.
            $ErrorActionPreference = 'Continue'
            $probe = @(& $launcher.Source -3.12 -c "import sys; print(sys.executable)" 2>$null)
            $probeExitCode = $LASTEXITCODE
        }
        catch {
            $probe = @()
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $candidate = if ($probe.Count -gt 0) { [string]$probe[0] } else { '' }
        $candidate = $candidate.Trim()
        if ($probeExitCode -eq 0 -and $candidate -and (Test-Path -LiteralPath $candidate)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    $roots = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Python'),
        (Join-Path $env:ProgramFiles 'Python'),
        (Join-Path ${env:ProgramFiles(x86)} 'Python')
    ) | Where-Object { $_ }
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidates = Get-ChildItem -LiteralPath $root -Directory -Filter 'Python312*' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'python.exe' }
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate) {
                return [IO.Path]::GetFullPath($candidate)
            }
        }
    }
    return $null
}

$python312 = Find-Python312
if (-not $python312) {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        throw 'LilacMacro could not automatically install Python 3.12 because Windows App Installer is unavailable.'
    }
    & $winget.Source install --id Python.Python.3.12 --exact --scope user --silent --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw 'LilacMacro could not automatically install Python 3.12.'
    }
    $python312 = Find-Python312
}
if (-not $python312) {
    throw 'LilacMacro installed Python 3.12 but could not locate its interpreter.'
}

$gpu = $null
$runtime = $null
if ($Device -eq 'gpu') {
    $gpu = Get-LilacNvidiaGpu
    $runtime = Resolve-LilacNvidiaOcrRuntime -Name $gpu.Name -ComputeCapability $gpu.ComputeCapability
}

New-Item -ItemType Directory -Path $ocrRoot -Force | Out-Null
if (Test-Path -LiteralPath $runtimeMarker) {
    Remove-Item -LiteralPath $runtimeMarker -Force
}
if (Test-Path -LiteralPath $profileMarker) {
    Remove-Item -LiteralPath $profileMarker -Force
}
if (-not (Test-Path -LiteralPath $venvPython)) {
    & $python312 -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the LilacMacro OCR environment.' }
}

& $venvPython -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'Could not update pip in the LilacMacro OCR environment.' }

& $venvPython -m pip uninstall --disable-pip-version-check -y paddlepaddle paddlepaddle-gpu

if ($Device -eq 'gpu') {
    & $venvPython -m pip install --disable-pip-version-check "paddlepaddle-gpu==$($runtime.PaddleVersion)" -i $runtime.PackageIndex
    if ($LASTEXITCODE -ne 0) { throw "Could not install the PaddlePaddle $($runtime.CudaFeed) runtime for $($runtime.Generation)." }
}
else {
    & $venvPython -m pip install --disable-pip-version-check 'paddlepaddle==3.3.0' -i 'https://www.paddlepaddle.org.cn/packages/stable/cpu/'
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the PaddlePaddle CPU runtime.' }
}

& $venvPython -m pip install --disable-pip-version-check 'paddleocr==3.7.0'
if ($LASTEXITCODE -ne 0) { throw 'Could not install PaddleOCR.' }

& $venvPython -c "import paddle, paddleocr; assert '$Device' != 'gpu' or (paddle.device.is_compiled_with_cuda() and paddle.device.cuda.device_count() > 0); print(f'OCR ready: PaddlePaddle {paddle.__version__}, PaddleOCR {paddleocr.__version__}')"
if ($LASTEXITCODE -ne 0) { throw 'The OCR environment was installed but failed its import check.' }

$profile = [ordered]@{
    SchemaVersion = 1
    Device = $Device
    PaddleVersion = '3.3.0'
    PaddleOcrVersion = '3.7.0'
}
if ($Device -eq 'gpu') {
    $profile['GpuIndex'] = $gpu.Index
    $profile['GpuName'] = $gpu.Name
    $profile['GpuGeneration'] = $runtime.Generation
    $profile['ComputeCapability'] = $gpu.ComputeCapability
    $profile['DriverVersion'] = $gpu.DriverVersion
    $profile['CudaFeed'] = $runtime.CudaFeed
}
[IO.File]::WriteAllText(
    $profileMarker,
    ($profile | ConvertTo-Json),
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($runtimeMarker, $Device, [Text.UTF8Encoding]::new($false))
