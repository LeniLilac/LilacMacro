<#
.SYNOPSIS
Renders selected PNG evidence from a LilacMacro deep-debug ZIP or expanded session.

.EXAMPLE
./scripts/New-DeepDebugContactSheet.ps1 "<path from OPEN DEEP DEBUG FOLDER>\deep-debug-story-wire-test.zip"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$InputPath,

    [ValidateRange(1, 200)]
    [int]$MaximumFrames = 24,

    [ValidateRange(1, 8)]
    [int]$Columns = 4,

    [ValidateRange(280, 800)]
    [int]$CellWidth = 420,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Select-Evenly {
    param([object[]]$Items, [int]$Maximum)
    if ($Items.Count -le $Maximum) { return @($Items) }
    if ($Maximum -eq 1) { return @($Items[0]) }
    $selected = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Maximum; $index++) {
        $sourceIndex = [int][Math]::Round($index * ($Items.Count - 1) / ($Maximum - 1))
        $selected.Add($Items[$sourceIndex])
    }
    return @($selected)
}

function Limit-Text {
    param([string]$Value, [int]$MaximumLength)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    if ($Value.Length -le $MaximumLength) { return $Value }
    return $Value.Substring(0, $MaximumLength - 1) + [char]0x2026
}

$resolvedInput = [System.IO.Path]::GetFullPath($InputPath)
if (-not (Test-Path -LiteralPath $resolvedInput)) { throw "Deep-debug input does not exist: $resolvedInput" }

$temporaryDirectory = $null
$sessionDirectory = $resolvedInput
try {
    if ([System.IO.Path]::GetExtension($resolvedInput).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
        $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("LilacMacro-deep-debug-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
        Expand-Archive -LiteralPath $resolvedInput -DestinationPath $temporaryDirectory
        $sessionDirectory = $temporaryDirectory
    }

    $eventPath = Join-Path $sessionDirectory 'events.jsonl'
    if (-not (Test-Path -LiteralPath $eventPath)) { throw 'The deep-debug session does not contain events.jsonl.' }
    $retainedFramePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $frameDirectory = Join-Path $sessionDirectory 'frames'
    if (Test-Path -LiteralPath $frameDirectory) {
        foreach ($retainedFrame in [System.IO.Directory]::EnumerateFiles($frameDirectory, '*.png')) {
            [void]$retainedFramePaths.Add([System.IO.Path]::GetFullPath($retainedFrame))
        }
    }
    $records = [System.Collections.Generic.List[object]]::new()
    $inputs = [System.Collections.Generic.List[object]]::new()
    foreach ($line in [System.IO.File]::ReadLines($eventPath)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $event = $line | ConvertFrom-Json
        if ($event.category -eq 'input' -and $event.action -in @('click_started', 'drag_started')) {
            $inputs.Add([pscustomobject]@{
                Sequence = [long]$event.sequence
                Action = [string]$event.action
                ClientWidth = [int]$event.data.clientSize.width
                ClientHeight = [int]$event.data.clientSize.height
                Data = $event.data.data
            })
            continue
        }
        if ($event.category -ne 'frame' -or [string]::IsNullOrWhiteSpace([string]$event.artifact)) { continue }
        $relative = ([string]$event.artifact).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $path = [System.IO.Path]::GetFullPath((Join-Path $sessionDirectory $relative))
        $sessionRoot = [System.IO.Path]::GetFullPath($sessionDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $path.StartsWith($sessionRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Frame path escaped the session: $relative"
        }
        if (-not $retainedFramePaths.Contains($path)) { continue }
        $records.Add([pscustomobject]@{
            Number = $records.Count + 1
            Sequence = [long]$event.sequence
            Timestamp = [DateTimeOffset]::Parse([string]$event.timestampUtc, [Globalization.CultureInfo]::InvariantCulture)
            Path = $path
            Source = [string]$event.action
            Detail = if ($null -eq $event.data) { '' } else { $event.data | ConvertTo-Json -Compress -Depth 4 }
            Inputs = [System.Collections.Generic.List[object]]::new()
        })
    }
    if ($records.Count -eq 0) { throw 'The deep-debug session contains no retained PNG evidence.' }
    $selected = @(Select-Evenly @($records) $MaximumFrames)
    $selectedLive = @($selected | Where-Object Source -eq 'live-client')
    foreach ($input in $inputs) {
        $owner = $selectedLive |
            Where-Object Sequence -lt $input.Sequence |
            Select-Object -Last 1
        if ($null -eq $owner -and $selectedLive.Count -gt 0) { $owner = $selectedLive[0] }
        if ($null -ne $owner) { $owner.Inputs.Add($input) }
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $repository = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
        $outputDirectory = Join-Path $repository 'artifacts\diagnostic-contact-sheets'
        $sourceName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedInput)
        $OutputPath = Join-Path $outputDirectory "$sourceName.png"
    }
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null

    Add-Type -AssemblyName System.Drawing
    $imageHeight = [int][Math]::Round($CellWidth * 0.56)
    $labelHeight = 58
    $cellHeight = $imageHeight + $labelHeight
    $rows = [int][Math]::Ceiling($selected.Count / $Columns)
    $sheet = [System.Drawing.Bitmap]::new($Columns * $CellWidth, $rows * $cellHeight)
    $graphics = [System.Drawing.Graphics]::FromImage($sheet)
    $graphics.Clear([System.Drawing.Color]::FromArgb(24, 21, 25))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $font = [System.Drawing.Font]::new('Consolas', 10, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $primary = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $secondary = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(208, 188, 202))
    $overlayBack = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(210, 24, 21, 25))
    $clickBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 63, 169))
    $dragBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 219, 77))
    $clickPen = [System.Drawing.Pen]::new($clickBrush.Color, 3)
    $dragPen = [System.Drawing.Pen]::new($dragBrush.Color, 3)
    $actionFont = [System.Drawing.Font]::new('Consolas', 10, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    try {
        for ($index = 0; $index -lt $selected.Count; $index++) {
            $record = $selected[$index]
            if (-not (Test-Path -LiteralPath $record.Path)) { throw "Retained frame is missing: $($record.Path)" }
            $left = ($index % $Columns) * $CellWidth
            $top = [int][Math]::Floor($index / $Columns) * $cellHeight
            $frame = [System.Drawing.Image]::FromFile($record.Path)
            try {
                $scale = [Math]::Min($CellWidth / $frame.Width, $imageHeight / $frame.Height)
                $width = [int][Math]::Round($frame.Width * $scale)
                $height = [int][Math]::Round($frame.Height * $scale)
                $x = $left + [int](($CellWidth - $width) / 2)
                $y = $top + [int](($imageHeight - $height) / 2)
                $graphics.DrawImage($frame, $x, $y, $width, $height)
                foreach ($input in $record.Inputs) {
                    if ($input.ClientWidth -lt 1 -or $input.ClientHeight -lt 1) { continue }
                    $scaleX = $width / [double]$input.ClientWidth
                    $scaleY = $height / [double]$input.ClientHeight
                    if ($input.Action -eq 'click_started') {
                        $pointX = $x + [double]$input.Data.point.x * $scaleX
                        $pointY = $y + [double]$input.Data.point.y * $scaleY
                        $graphics.DrawEllipse($clickPen, [float]($pointX - 8), [float]($pointY - 8), 16, 16)
                        $graphics.DrawLine($clickPen, [float]($pointX - 12), [float]$pointY, [float]($pointX + 12), [float]$pointY)
                        $graphics.DrawLine($clickPen, [float]$pointX, [float]($pointY - 12), [float]$pointX, [float]($pointY + 12))
                        $label = "CLICK $($input.Data.point.x),$($input.Data.point.y)"
                        $labelSize = $graphics.MeasureString($label, $actionFont)
                        $labelX = [Math]::Min($x + $width - $labelSize.Width - 4, $pointX + 10)
                        $labelY = [Math]::Max($y, $pointY - $labelSize.Height - 7)
                        $graphics.FillRectangle($overlayBack, [float]$labelX, [float]$labelY, [float]($labelSize.Width + 4), [float]($labelSize.Height + 2))
                        $graphics.DrawString($label, $actionFont, $clickBrush, [float]($labelX + 2), [float]($labelY + 1))
                    }
                    elseif ($input.Action -eq 'drag_started') {
                        $startX = $x + [double]$input.Data.start.x * $scaleX
                        $startY = $y + [double]$input.Data.start.y * $scaleY
                        $endX = $x + [double]$input.Data.end.x * $scaleX
                        $endY = $y + [double]$input.Data.end.y * $scaleY
                        $graphics.DrawLine($dragPen, [float]$startX, [float]$startY, [float]$endX, [float]$endY)
                        $graphics.FillEllipse($dragBrush, [float]($startX - 5), [float]($startY - 5), 10, 10)
                        $graphics.FillEllipse($dragBrush, [float]($endX - 7), [float]($endY - 7), 14, 14)
                        $label = "DRAG $($input.Data.start.x),$($input.Data.start.y) > $($input.Data.end.x),$($input.Data.end.y)"
                        $labelSize = $graphics.MeasureString($label, $actionFont)
                        $labelX = [Math]::Min($x + $width - $labelSize.Width - 4, ($startX + $endX) / 2 + 8)
                        $labelY = [Math]::Max($y, ($startY + $endY) / 2 - $labelSize.Height - 7)
                        $graphics.FillRectangle($overlayBack, [float]$labelX, [float]$labelY, [float]($labelSize.Width + 4), [float]($labelSize.Height + 2))
                        $graphics.DrawString($label, $actionFont, $dragBrush, [float]($labelX + 2), [float]($labelY + 1))
                    }
                }
            }
            finally { $frame.Dispose() }
            $graphics.DrawString(("#{0:D4} {1} {2}" -f $record.Number, $record.Timestamp.ToLocalTime().ToString('HH:mm:ss.fff'), (Limit-Text $record.Source 30)), $font, $primary, $left + 6, $top + $imageHeight + 5)
            $graphics.DrawString((Limit-Text $record.Detail 74), $font, $secondary, $left + 6, $top + $imageHeight + 27)
        }
        $sheet.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $actionFont.Dispose()
        $clickPen.Dispose()
        $dragPen.Dispose()
        $clickBrush.Dispose()
        $dragBrush.Dispose()
        $overlayBack.Dispose()
        $font.Dispose()
        $primary.Dispose()
        $secondary.Dispose()
        $graphics.Dispose()
        $sheet.Dispose()
    }

    [pscustomobject]@{ Output = $resolvedOutput; Frames = $selected.Count }
}
finally {
    if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
        $resolvedTemporary = [System.IO.Path]::GetFullPath($temporaryDirectory)
        $systemTemporary = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedTemporary.StartsWith($systemTemporary, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected temporary directory: $resolvedTemporary"
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
