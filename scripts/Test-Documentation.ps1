[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$repositoryPrefix = $repositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$failures = [System.Collections.Generic.List[string]]::new()

$requiredFiles = @(
    'README.md'
    'AGENTS.md'
    'CONTRIBUTING.md'
    'PRIVACY.md'
    'PRODUCT.md'
    'DESIGN.md'
    'docs/README.md'
    'docs/PROJECT-STATUS.md'
    'docs/DEVELOPMENT.md'
    'docs/TESTING.md'
    'docs/GAME-BEHAVIOR.md'
    'docs/OCR-AND-VISION.md'
    'docs/PLACEMENT-AUTHORING.md'
    'docs/MACRO-ARCHITECTURE.md'
    'docs/TROUBLESHOOTING.md'
    'docs/ARCHITECTURE.md'
    'docs/DATASET-FORMAT.md'
    'docs/AGENT-DATASET-WORKFLOW.md'
    'src/LilacMacro.App/AGENTS.md'
    'src/LilacMacro.Core/AGENTS.md'
    'src/LilacMacro.Windows/AGENTS.md'
    'tools/AGENTS.md'
    'tests/AGENTS.md'
)

$indexTargets = @(
    'README.md'
    'AGENTS.md'
    'CONTRIBUTING.md'
    'PRIVACY.md'
    'PRODUCT.md'
    'DESIGN.md'
    'docs/PROJECT-STATUS.md'
    'docs/DEVELOPMENT.md'
    'docs/TESTING.md'
    'docs/TROUBLESHOOTING.md'
    'docs/ARCHITECTURE.md'
    'docs/GAME-BEHAVIOR.md'
    'docs/OCR-AND-VISION.md'
    'docs/PLACEMENT-AUTHORING.md'
    'docs/MACRO-ARCHITECTURE.md'
    'docs/DATASET-FORMAT.md'
    'docs/AGENT-DATASET-WORKFLOW.md'
    'schemas/dataset.schema.json'
    'src/LilacMacro.App/AGENTS.md'
    'src/LilacMacro.Core/AGENTS.md'
    'src/LilacMacro.Windows/AGENTS.md'
    'tools/AGENTS.md'
    'tests/AGENTS.md'
)

$agentRouteTargets = @(
    'CONTRIBUTING.md'
    'docs/DEVELOPMENT.md'
    'docs/PROJECT-STATUS.md'
    'docs/ARCHITECTURE.md'
    'docs/GAME-BEHAVIOR.md'
    'docs/OCR-AND-VISION.md'
    'docs/PLACEMENT-AUTHORING.md'
    'docs/MACRO-ARCHITECTURE.md'
    'docs/DATASET-FORMAT.md'
    'docs/AGENT-DATASET-WORKFLOW.md'
    'PRIVACY.md'
    'docs/TESTING.md'
)

$statusFiles = @(
    'CONTRIBUTING.md'
    'PRIVACY.md'
    'PRODUCT.md'
    'DESIGN.md'
    'docs/README.md'
    'docs/PROJECT-STATUS.md'
    'docs/DEVELOPMENT.md'
    'docs/TESTING.md'
    'docs/GAME-BEHAVIOR.md'
    'docs/OCR-AND-VISION.md'
    'docs/PLACEMENT-AUTHORING.md'
    'docs/MACRO-ARCHITECTURE.md'
    'docs/TROUBLESHOOTING.md'
    'docs/ARCHITECTURE.md'
    'docs/DATASET-FORMAT.md'
    'docs/AGENT-DATASET-WORKFLOW.md'
)

function ConvertTo-RepositoryPath([string]$path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if ($fullPath.Equals($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) { return '.' }
    return $fullPath.Substring($repositoryPrefix.Length).Replace('\', '/')
}

function Get-MarkdownAnchor([string]$heading) {
    $value = [Text.RegularExpressions.Regex]::Replace(
        $heading,
        '!\[([^\]]*)\]\([^)]*\)',
        '$1')
    $value = [Text.RegularExpressions.Regex]::Replace(
        $value,
        '\[([^\]]+)\]\([^)]*\)',
        '$1')
    $value = [Text.RegularExpressions.Regex]::Replace($value, '<[^>]+>', '')
    $value = $value.Replace('`', '').ToLowerInvariant()
    $value = [Text.RegularExpressions.Regex]::Replace($value, '[^\p{L}\p{Nd}\-_ ]', '')
    return [Text.RegularExpressions.Regex]::Replace($value.Trim(), '\s', '-')
}

function Get-MarkdownAnchors([string]$path) {
    $anchors = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $counts = @{}
    $inFence = $false
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^\s*(```|~~~)') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence -or $line -notmatch '^\s{0,3}#{1,6}\s+(?<heading>.+?)\s*#*\s*$') {
            continue
        }

        $anchor = Get-MarkdownAnchor $Matches.heading
        if (-not $anchor) { continue }
        $count = if ($counts.ContainsKey($anchor)) { [int]$counts[$anchor] } else { 0 }
        $resolved = if ($count -eq 0) { $anchor } else { "$anchor-$count" }
        $counts[$anchor] = $count + 1
        [void]$anchors.Add($resolved)
    }
    return $anchors
}

function Get-LinkTargets([string]$path) {
    $targets = [System.Collections.Generic.List[object]]::new()
    $lineNumber = 0
    $inFence = $false
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        if ($line -match '^\s*(```|~~~)') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        $inlineMatches = [Text.RegularExpressions.Regex]::Matches(
            $line,
            '!?\[[^\]]*\]\(\s*(?<target><[^>]+>|[^\s\)]+)')
        foreach ($match in $inlineMatches) {
            $targets.Add([pscustomobject]@{
                Line = $lineNumber
                Target = $match.Groups['target'].Value.Trim('<', '>')
            })
        }

        if ($line -match '^\s*\[[^\]]+\]:\s*(?<target><[^>]+>|\S+)') {
            $targets.Add([pscustomobject]@{
                Line = $lineNumber
                Target = $Matches.target.Trim('<', '>')
            })
        }
    }
    return $targets
}

function Resolve-LocalTarget(
    [string]$sourcePath,
    [string]$target,
    [int]$lineNumber,
    [System.Collections.Generic.HashSet[string]]$resolvedTargets) {
    if (-not $target -or $target.StartsWith('//') -or
        $target -match '^(?i:https?|mailto|tel|data):') {
        return
    }

    $parts = $target.Split('#', 2)
    $pathPart = [Uri]::UnescapeDataString($parts[0])
    $fragment = if ($parts.Count -eq 2) { [Uri]::UnescapeDataString($parts[1]) } else { '' }
    if ([IO.Path]::IsPathRooted($pathPart)) {
        $failures.Add("$sourcePath`:$lineNumber uses absolute local link '$target'.")
        return
    }

    $sourceFullPath = Join-Path $repositoryRoot $sourcePath
    $targetFullPath = if ($pathPart) {
        [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $sourceFullPath) $pathPart))
    }
    else {
        $sourceFullPath
    }

    if ($targetFullPath -ne $repositoryRoot -and
        -not $targetFullPath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("$sourcePath`:$lineNumber links outside the repository: '$target'.")
        return
    }
    if (-not (Test-Path -LiteralPath $targetFullPath)) {
        $failures.Add("$sourcePath`:$lineNumber has missing local link '$target'.")
        return
    }

    $repositoryTarget = ConvertTo-RepositoryPath $targetFullPath
    [void]$resolvedTargets.Add($repositoryTarget)
    if (-not $fragment) { return }
    if ((Get-Item -LiteralPath $targetFullPath) -isnot [IO.FileInfo] -or
        [IO.Path]::GetExtension($targetFullPath) -notin '.md', '.markdown') {
        $failures.Add("$sourcePath`:$lineNumber links to a heading on a non-Markdown target: '$target'.")
        return
    }

    $anchors = Get-MarkdownAnchors $targetFullPath
    if (-not $anchors.Contains($fragment)) {
        $failures.Add("$sourcePath`:$lineNumber has missing heading '#$fragment' in '$repositoryTarget'.")
    }
}

foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $required) -PathType Leaf)) {
        $failures.Add("Required documentation file is missing: $required")
    }
}

foreach ($statusFile in $statusFiles) {
    $statusPath = Join-Path $repositoryRoot $statusFile
    $statusHeader = if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
        (Get-Content -LiteralPath $statusPath -TotalCount 12) -join [Environment]::NewLine
    }
    else { '' }
    if ($statusHeader -and $statusHeader -notmatch
        '(?im)^\*\*Status:\s*(Current|Implemented|Prototype|Planned|Unresolved)') {
        $failures.Add("$statusFile does not declare a canonical status near its title.")
    }
}

Push-Location $repositoryRoot
try {
    $markdownFiles = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw 'Git failed while enumerating Markdown files.' }
}
finally {
    Pop-Location
}

$linksBySource = @{}
$personalPathPattern = '(?i)(?:[A-Z]:\\Users\\[^\\/\s]+|[A-Z]:/Users/[^/\s]+|/Users/[^/\s]+|/home/[^/\s]+)'
foreach ($markdownFile in $markdownFiles) {
    $source = $markdownFile.Replace('\', '/')
    $fullPath = Join-Path $repositoryRoot $markdownFile
    $content = Get-Content -LiteralPath $fullPath -Raw
    if ($content -match $personalPathPattern) {
        $failures.Add("$source contains a personal absolute path.")
    }

    $resolvedTargets = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($link in Get-LinkTargets $fullPath) {
        Resolve-LocalTarget $source $link.Target $link.Line $resolvedTargets
    }
    $linksBySource[$source] = $resolvedTargets
}

foreach ($target in $indexTargets) {
    if (-not $linksBySource.ContainsKey('docs/README.md') -or
        -not $linksBySource['docs/README.md'].Contains($target)) {
        $failures.Add("docs/README.md does not link canonical document '$target'.")
    }
}

foreach ($target in $agentRouteTargets) {
    if (-not $linksBySource.ContainsKey('AGENTS.md') -or
        -not $linksBySource['AGENTS.md'].Contains($target)) {
        $failures.Add("AGENTS.md does not route agents to '$target'.")
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error -Message $failure -ErrorAction Continue
    }
    exit 1
}

$successMessage = "Documentation passed: {0} Markdown files, local links and headings valid, " +
    "canonical status/index complete, agent routes complete, no personal paths."
Write-Output ($successMessage -f $markdownFiles.Count)
