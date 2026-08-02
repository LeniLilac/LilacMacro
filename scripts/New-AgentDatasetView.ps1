[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$DatasetDirectory,

    [Parameter(Position = 1)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$datasetPath = (Resolve-Path -LiteralPath $DatasetDirectory).Path
$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tools\LilacMacro.DatasetTool\LilacMacro.DatasetTool.csproj'),
    '--',
    'agent-view',
    $datasetPath
)
if ($OutputDirectory) {
    $arguments += [IO.Path]::GetFullPath($OutputDirectory)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
