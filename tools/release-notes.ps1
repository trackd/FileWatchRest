#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generate release notes from git history

.DESCRIPTION
    Creates release notes by parsing git commit history since the last tag.
    Can be run locally or in CI/CD pipelines.

.PARAMETER Version
    Version string for the release (e.g., 1.2.3)

.PARAMETER OutputPath
    Output file path for release notes (default: ./release_notes.md)

.PARAMETER Repository
    GitHub repository in format owner/repo (e.g., trackd/FileWatchRest)

.PARAMETER CommitSha
    Git commit SHA for build information

.EXAMPLE
    # Local generation (auto-detect version and repo)
    .\tools\release-notes.ps1

.EXAMPLE
    # CI generation with parameters
    .\tools\release-notes.ps1 -Version "1.2.3" -Repository "trackd/FileWatchRest" -CommitSha "abc123"
#>
param(
    [string]$Version,
    [string]$OutputPath,
    [string]$Repository,
    [string]$CommitSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$parent = Split-Path $PSScriptRoot -Parent
# Load project XML once for reuse
$csproj = $null
$projPath = Join-Path $parent 'FileWatchRest' 'FileWatchRest.csproj'
if (Test-Path $projPath) { [xml]$csproj = Get-Content $projPath }

Write-Host "==> Generating Release Notes" -ForegroundColor Cyan
if (-not $OutputPath) {
    $OutputPath = './release_notes.md'
}
# Detect version if not provided (use loaded csproj when available)
if (-not $Version) {
    Write-Host "Detecting version from FileWatchRest.csproj..." -ForegroundColor Gray
    if ($csproj) { $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
    if (-not $Version) { $Version = "0.0.0" }
}

# Detect repository if not provided
if (-not $Repository) {
    try {
        $remoteUrl = git remote get-url origin 2>$null
        if ($remoteUrl -match 'github\.com[:/](.+?)(?:\.git)?$') {
            $Repository = $matches[1]
            Write-Host "Detected repository: $Repository" -ForegroundColor Gray
        }
    } catch {
        $Repository = "trackd/FileWatchRest"
    }
}

# Detect commit SHA if not provided
if (-not $CommitSha) {
    try {
        $CommitSha = git rev-parse HEAD 2>$null
        if ($CommitSha) {
            $CommitSha = $CommitSha.Substring(0, [Math]::Min(7, $CommitSha.Length))
        }
    } catch {
        $CommitSha = "unknown"
    }
}

# Determine tags and the previous tag (the tag before HEAD if HEAD is tagged)
$lastTag = $null
try {
    $tags = @()
    $rawTags = git for-each-ref --sort=-creatordate --format '%(refname:short)' refs/tags 2>$null
    if ($rawTags) { $tags = $rawTags -split "`n" | Where-Object { $_ } }

    # Tags pointing at HEAD (may be empty)
    $currentTagsRaw = git tag --points-at HEAD 2>$null
    $currentTags = @()
    if ($currentTagsRaw) { $currentTags = $currentTagsRaw -split "`n" | Where-Object { $_ } }

    if ($currentTags.Count -gt 0 -and $tags.Count -gt 0) {
        $currentTag = $currentTags[0]
        $idx = $tags.IndexOf($currentTag)
        if ($idx -ge 0 -and ($idx + 1) -lt $tags.Count) {
            $lastTag = $tags[$idx + 1]
        }
        else {
            # No previous tag (this is the first tag)
            $lastTag = $null
        }
    }
    else {
        # Use the most recent tag as the baseline if HEAD is not tagged
        if ($tags.Count -gt 0) { $lastTag = $tags[0] }
    }

    if ($lastTag) { Write-Host "Last tag: $lastTag" -ForegroundColor Gray } else { Write-Host "No previous tag found" -ForegroundColor Gray }
}
catch {
    Write-Host "Failed to determine tags: $_" -ForegroundColor Gray
}

# Generate commit list
$commits = @()
try {
    if ($lastTag) {
        # Get commits since last tag
        $commitList = git log --pretty=format:"- %s (%h)" "$lastTag..HEAD" 2>$null
    } else {
        # Get last 20 commits if no tags exist
        $commitList = git log --pretty=format:"- %s (%h)" -20 2>$null
    }

    if ($commitList) {
        $commits = $commitList -split "`n" | Where-Object { $_ }
    }
} catch {
    Write-Warning "Failed to retrieve git history: $_"
}

if (@($commits).Count -eq 0) {
    $commits = @("- Initial release")
}

# Determine if pre-release
$isPreRelease = $Version -match '-'
$releaseType = if ($isPreRelease) { "Pre-release" } else { "Release" }
$tf = 'unknown'
$runtime = 'unknown'
try {
    if ($csproj) {
        $tf = $csproj.Project.PropertyGroup | ForEach-Object { $_.TargetFramework } | Where-Object { $_ } | Select-Object -First 1
        if ($tf -eq 'unknown') { $tf = $csproj.Project.PropertyGroup | ForEach-Object { $_.TargetFrameworks } | Where-Object { $_ } | Select-Object -First 1 }
        $runtime = $csproj.Project.PropertyGroup | ForEach-Object { $_.RuntimeIdentifier } | Where-Object { $_ } | Select-Object -First 1
        if ($runtime -eq 'unknown') { $runtime = $csproj.Project.PropertyGroup | ForEach-Object { $_.RuntimeIdentifiers } | Where-Object { $_ } | Select-Object -First 1 }
    }
}
catch { }

# Build release notes
$notes = @"
## FileWatchRest v$Version

### What's Changed

$($commits -join "`n")

### Build Information

- **Type**: $releaseType
- **Version**: $Version
- **Commit**: $CommitSha
- **Build Date**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")

### Determine target framework and runtime from project

- **TargetFramework**: $tf
- **Runtime**: $runtime

### Links

- [Documentation](https://github.com/$Repository/blob/main/README.md)
- [Issues](https://github.com/$Repository/issues)
- [Changelog](https://github.com/$Repository/compare/$lastTag...v$Version)

---

- [Full Changelog](https://github.com/$Repository/commits/v$Version)
"@

# Write to file
Set-Content -Path $OutputPath -Value $notes -Encoding UTF8
Write-Host "Generated release notes: $OutputPath" -ForegroundColor Green

# Preview
Write-Host "`nPreview:" -ForegroundColor Cyan
Write-Host "---" -ForegroundColor DarkGray
Write-Host $notes
Write-Host "---" -ForegroundColor DarkGray
