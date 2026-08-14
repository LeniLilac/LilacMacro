[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OcrGpuRuntime.ps1')

function Assert-Profile {
    param(
        [double]$Capability,
        [string]$Generation,
        [string]$CudaFeed
    )

    $profile = Resolve-LilacNvidiaOcrRuntime -Name 'Test GPU' -ComputeCapability $Capability
    if ($profile.Generation -ne $Generation -or $profile.CudaFeed -ne $CudaFeed) {
        throw "Compute capability $Capability resolved to $($profile.Generation)/$($profile.CudaFeed), expected $Generation/$CudaFeed."
    }
    if ($profile.PaddleVersion -ne '3.3.0') {
        throw "Compute capability $Capability resolved to unexpected Paddle version $($profile.PaddleVersion)."
    }
}

Assert-Profile 6.0 'Pascal' 'cu118'
Assert-Profile 6.1 'Pascal' 'cu118'
Assert-Profile 7.0 'Volta' 'cu118'
Assert-Profile 7.5 'Turing' 'cu126'
Assert-Profile 8.0 'Ampere' 'cu126'
Assert-Profile 8.6 'Ampere' 'cu126'
Assert-Profile 8.9 'Ada' 'cu126'
Assert-Profile 9.0 'Hopper' 'cu129'
Assert-Profile 10.0 'Blackwell' 'cu129'
Assert-Profile 12.0 'Blackwell' 'cu129'

foreach ($unsupported in @(5.2, 5.3)) {
    try {
        $null = Resolve-LilacNvidiaOcrRuntime -Name 'Unsupported GPU' -ComputeCapability $unsupported
        throw "Compute capability $unsupported should have been rejected."
    }
    catch {
        if ($_.Exception.Message -notmatch 'require 6\.0 or newer') { throw }
    }
}

Write-Output 'OCR GPU runtime policy passed.'
