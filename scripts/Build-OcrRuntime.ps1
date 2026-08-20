[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ocrRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) 'ocr'
$pythonRoot = Join-Path $ocrRoot 'python'
$modelRoot = Join-Path $ocrRoot 'models'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('LilacMacro-ocr-build-' + [Guid]::NewGuid().ToString('N'))
$modelCache = Join-Path $temporaryRoot 'model-cache'

function Invoke-BuilderPython([string[]]$Arguments) {
    & $builderPython @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Python build command failed with exit code $LASTEXITCODE." }
}

function Invoke-BundledPython([string[]]$Arguments) {
    & $bundledPython @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Bundled Python command failed with exit code $LASTEXITCODE." }
}

function Resolve-Python312 {
    $candidates = @()
    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -ne $launcher) {
        try {
            $candidate = @(& $launcher.Source -3.12 -c 'import sys; print(sys.executable)' 2>$null)
            if ($candidate.Count -gt 0) { $candidates += [string]$candidate[0] }
        }
        catch {
            # The launcher may exist without a registered 3.12 interpreter.
        }
    }

    $python = Get-Command python.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $python -and -not [string]::IsNullOrWhiteSpace($python.Source)) {
        $candidates += $python.Source
    }

    foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        try {
            $path = [IO.Path]::GetFullPath(([string]$candidate).Trim())
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            $version = (& $path -c 'import sys; print(str(sys.version_info.major) + chr(46) + str(sys.version_info.minor))' 2>$null).Trim()
            if ($version -eq '3.12') { return $path }
        }
        catch {
            # A command shim or stale launcher entry is not a usable builder.
        }
    }

    return $null
}

try {
    $builderPython = Resolve-Python312
    if ([string]::IsNullOrWhiteSpace($builderPython)) {
        throw 'Python 3.12 is required to build the bundled OCR runtime.'
    }
    $pythonVersion = (& $builderPython -c 'import sys; print(str(sys.version_info.major) + chr(46) + str(sys.version_info.minor))').Trim()
    if ($pythonVersion -ne '3.12') { throw "The OCR build Python must be 3.12, not $pythonVersion." }
    $sourceRoot = (& $builderPython -c 'import sys; print(sys.prefix)').Trim()
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        throw 'The Python 3.12 installation root could not be located.'
    }

    if (Test-Path -LiteralPath $ocrRoot) {
        Remove-Item -LiteralPath $ocrRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $pythonRoot, $modelRoot, $temporaryRoot, $modelCache | Out-Null

    $runtimeFiles = @(
        Get-ChildItem -LiteralPath $sourceRoot -File -Filter 'python*.dll' -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $sourceRoot -File -Filter 'vcruntime*.dll' -ErrorAction SilentlyContinue
        Get-Item -LiteralPath (Join-Path $sourceRoot 'python.exe') -ErrorAction SilentlyContinue
        Get-Item -LiteralPath (Join-Path $sourceRoot 'pythonw.exe') -ErrorAction SilentlyContinue
    ) | Where-Object { $_ }
    foreach ($source in $runtimeFiles) {
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $pythonRoot $source.Name)
    }
    foreach ($directory in @('DLLs', 'Lib')) {
        $source = Join-Path $sourceRoot $directory
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            throw "The Python runtime is missing $directory."
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $pythonRoot $directory) -Recurse -Force
    }

    $bundledPython = Join-Path $pythonRoot 'python.exe'
    $sitePackages = Join-Path $pythonRoot 'Lib\site-packages'
    New-Item -ItemType Directory -Path $sitePackages -Force | Out-Null
    Invoke-BuilderPython @(
        '-m', 'pip', 'install',
        '--disable-pip-version-check',
        '--no-cache-dir',
        '--upgrade',
        '--target', $sitePackages,
        '--only-binary=:all:',
        '-i', 'https://www.paddlepaddle.org.cn/packages/stable/cpu/',
        '--extra-index-url', 'https://pypi.org/simple',
        'paddlepaddle==3.3.0',
        'paddleocr==3.7.0'
    )

    $previousCache = $env:PADDLE_PDX_CACHE_HOME
    $previousSource = $env:PADDLE_PDX_MODEL_SOURCE
    $previousCheck = $env:PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK
    $env:PADDLE_PDX_CACHE_HOME = $modelCache
    $env:PADDLE_PDX_MODEL_SOURCE = 'BOS'
    $env:PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK = 'True'
    try {
        Invoke-BundledPython @(
            '-c',
            "from paddleocr import PaddleOCR; [PaddleOCR(text_detection_model_name=det, text_recognition_model_name=rec, use_doc_orientation_classify=False, use_doc_unwarping=False, use_textline_orientation=False, device='cpu', enable_mkldnn=False) for det, rec in [('PP-OCRv6_small_det', 'PP-OCRv6_small_rec'), ('PP-OCRv6_tiny_det', 'PP-OCRv6_tiny_rec')]]"
        )
    }
    finally {
        $env:PADDLE_PDX_CACHE_HOME = $previousCache
        $env:PADDLE_PDX_MODEL_SOURCE = $previousSource
        $env:PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK = $previousCheck
    }

    foreach ($directory in @('test', 'idlelib', 'tkinter', 'turtledemo')) {
        $path = Join-Path $pythonRoot "Lib\$directory"
        if (Test-Path -LiteralPath $path -PathType Container) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    $cacheDirectories = @(Get-ChildItem -LiteralPath $pythonRoot -Directory -Filter '__pycache__' -Recurse -ErrorAction SilentlyContinue)
    foreach ($directory in $cacheDirectories) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath (Join-Path $modelCache 'official_models') -PathType Container)) {
        throw 'The bundled OCR model cache was not created.'
    }
    Copy-Item -LiteralPath (Join-Path $modelCache 'official_models') -Destination $modelRoot -Recurse -Force

    $manifest = [ordered]@{
        schemaVersion = 1
        python = $pythonVersion
        paddle = '3.3.0'
        paddleOcr = '3.7.0'
        device = 'cpu'
        modelPairs = @('PP-OCRv6_small', 'PP-OCRv6_tiny')
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText(
        (Join-Path $ocrRoot 'cpu-runtime.json'),
        $manifest,
        [Text.UTF8Encoding]::new($false))

    $license = Join-Path $sourceRoot 'LICENSE.txt'
    if (Test-Path -LiteralPath $license -PathType Leaf) {
        Copy-Item -LiteralPath $license -Destination (Join-Path $ocrRoot 'Python-LICENSE.txt')
    }
    Write-Output $ocrRoot
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
