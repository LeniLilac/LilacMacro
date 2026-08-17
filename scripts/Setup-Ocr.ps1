[CmdletBinding()]
param(
    [ValidateSet('cpu', 'gpu')]
    [string]$Device = 'cpu',
    [string]$InstallRoot = '',
    [string]$BundledPythonPath = '',
    [switch]$ProbeGpu
)

$ErrorActionPreference = 'Stop'

$ocrRoot = if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Join-Path $env:LOCALAPPDATA 'LilacMacro\ocr'
} else {
    [IO.Path]::GetFullPath($InstallRoot)
}
$gpuPolicy = Join-Path $PSScriptRoot 'OcrGpuRuntime.ps1'

if (-not (Test-Path -LiteralPath $gpuPolicy)) {
    throw 'LilacMacro could not locate the OCR GPU runtime policy.'
}
. $gpuPolicy

function Write-Stage([int]$Percent, [string]$Message) {
    Write-Output ("[OCR_STAGE] {0}|{1}" -f $Percent, $Message)
}

function Find-Python312 {
    if (-not [string]::IsNullOrWhiteSpace($BundledPythonPath) -and
        (Test-Path -LiteralPath $BundledPythonPath -PathType Leaf)) {
        return [IO.Path]::GetFullPath($BundledPythonPath)
    }

    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -ne $launcher) {
        $probe = @()
        $probeExitCode = 1
        $previousErrorActionPreference = $ErrorActionPreference
        try {
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

    $python = Get-Command python.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $python -and -not [string]::IsNullOrWhiteSpace($python.Source)) {
        try {
            $version = (& $python.Source -c "import sys; print(str(sys.version_info.major) + chr(46) + str(sys.version_info.minor))" 2>$null).Trim()
            if ($version -eq '3.12') {
                return [IO.Path]::GetFullPath($python.Source)
            }
        }
        catch {
            # A command shim or stale PATH entry is not a usable interpreter.
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

if ($ProbeGpu) {
    try {
        $gpu = Get-LilacNvidiaGpu
        $runtime = Resolve-LilacNvidiaOcrRuntime -Name $gpu.Name -ComputeCapability $gpu.ComputeCapability
        [ordered]@{
            Name = $gpu.Name
            Generation = $runtime.Generation
            ComputeCapability = $gpu.ComputeCapability
            DriverVersion = $gpu.DriverVersion
            CudaFeed = $runtime.CudaFeed
        } | ConvertTo-Json -Compress
        exit 0
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit 2
    }
}

$python312 = Find-Python312
if (-not $python312) {
    throw 'LilacMacro could not find its bundled Python 3.12 runtime. Repair the LilacMacro installation or use a development Python 3.12 installation.'
}

$runtimeRoot = if ($Device -eq 'gpu') { Join-Path $ocrRoot 'gpu' } else { $ocrRoot }
$venvRoot = Join-Path $runtimeRoot 'venv'
$venvPython = Join-Path $venvRoot 'Scripts\python.exe'
$runtimeMarker = Join-Path $runtimeRoot 'runtime-device.txt'
$profileMarker = Join-Path $runtimeRoot 'runtime-profile.json'

$gpu = $null
$runtime = $null
if ($Device -eq 'gpu') {
    Write-Stage 5 'Checking NVIDIA GPU compatibility.'
    $gpu = Get-LilacNvidiaGpu
    $runtime = Resolve-LilacNvidiaOcrRuntime -Name $gpu.Name -ComputeCapability $gpu.ComputeCapability
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
if (Test-Path -LiteralPath $runtimeMarker) { Remove-Item -LiteralPath $runtimeMarker -Force }
if (Test-Path -LiteralPath $profileMarker) { Remove-Item -LiteralPath $profileMarker -Force }
if ($Device -eq 'gpu' -and (Test-Path -LiteralPath $venvRoot)) {
    Write-Stage 10 'Removing the incomplete or previous GPU environment.'
    Remove-Item -LiteralPath $venvRoot -Recurse -Force
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Stage 15 'Creating the per-user OCR environment.'
    & $python312 -m venv $venvRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the LilacMacro OCR environment.' }
}

Write-Stage 25 'Preparing the OCR package installer.'
& $venvPython -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'Could not update pip in the LilacMacro OCR environment.' }

if ($Device -eq 'gpu') {
    Write-Stage 45 ("Installing the Paddle GPU runtime for {0} ({1})." -f $runtime.Generation, $runtime.CudaFeed)
    & $venvPython -m pip install --disable-pip-version-check "paddlepaddle-gpu==$($runtime.PaddleVersion)" -i $runtime.PackageIndex
    if ($LASTEXITCODE -ne 0) { throw "Could not install the PaddlePaddle $($runtime.CudaFeed) runtime for $($runtime.Generation)." }
}
else {
    Write-Stage 45 'Installing the CPU OCR runtime.'
    & $venvPython -m pip install --disable-pip-version-check 'paddlepaddle==3.3.0' -i 'https://www.paddlepaddle.org.cn/packages/stable/cpu/'
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the PaddlePaddle CPU runtime.' }
}

Write-Stage 70 'Installing PaddleOCR.'
& $venvPython -m pip install --disable-pip-version-check 'paddleocr==3.7.0'
if ($LASTEXITCODE -ne 0) { throw 'Could not install PaddleOCR.' }

Write-Stage 90 'Verifying the OCR runtime.'
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
Write-Stage 100 ("{0} OCR is ready." -f $Device.ToUpperInvariant())
