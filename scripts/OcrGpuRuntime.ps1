Set-StrictMode -Version 3.0

function Get-LilacNvidiaGpu {
    $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($null -eq $nvidiaSmi) {
        throw 'NVIDIA driver tools were not found. Use CPU OCR or install a current NVIDIA driver.'
    }

    $rows = @(& $nvidiaSmi.Source '--query-gpu=index,name,compute_cap,driver_version' '--format=csv,noheader,nounits' 2>&1)
    if ($LASTEXITCODE -ne 0 -or $rows.Count -eq 0) {
        throw 'LilacMacro could not query the NVIDIA GPU compute capability. Update the NVIDIA driver or use CPU OCR.'
    }

    $columns = @($rows[0] -split '\s*,\s*', 4)
    if ($columns.Count -ne 4) {
        throw 'The NVIDIA GPU query returned an unsupported result. Update the NVIDIA driver or use CPU OCR.'
    }

    [double]$computeCapability = 0
    if (-not [double]::TryParse(
        $columns[2],
        [Globalization.NumberStyles]::Number,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$computeCapability)) {
        throw 'The NVIDIA driver did not report a usable compute capability. Update the driver or use CPU OCR.'
    }

    [pscustomobject]@{
        Index = [int]$columns[0]
        Name = $columns[1].Trim()
        ComputeCapability = $computeCapability
        DriverVersion = $columns[3].Trim()
    }
}

function Resolve-LilacNvidiaOcrRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [double]$ComputeCapability
    )

    if ($ComputeCapability -lt 6.0) {
        throw "NVIDIA GPU '$Name' has compute capability $($ComputeCapability.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)); current Paddle GPU packages require 6.0 or newer. Use CPU OCR."
    }

    $generation = if ($ComputeCapability -ge 10.0) {
        'Blackwell'
    }
    elseif ($ComputeCapability -ge 9.0) {
        'Hopper'
    }
    elseif ($ComputeCapability -ge 8.9) {
        'Ada'
    }
    elseif ($ComputeCapability -ge 8.0) {
        'Ampere'
    }
    elseif ($ComputeCapability -ge 7.5) {
        'Turing'
    }
    elseif ($ComputeCapability -ge 7.0) {
        'Volta'
    }
    else {
        'Pascal'
    }
    $cudaFeed = if ($ComputeCapability -ge 9.0) {
        'cu129'
    }
    elseif ($ComputeCapability -ge 7.5) {
        'cu126'
    }
    else {
        'cu118'
    }

    [pscustomobject]@{
        Name = $Name
        Generation = $generation
        ComputeCapability = $ComputeCapability
        PaddleVersion = '3.3.0'
        CudaFeed = $cudaFeed
        PackageIndex = "https://www.paddlepaddle.org.cn/packages/stable/$cudaFeed/"
    }
}
